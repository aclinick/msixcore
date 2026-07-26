using System.Text.Json;
using MsixCore.Packaging;
using MsixCore.Packaging.Integrity;

namespace MsixMgr;

/// <summary>
/// <c>validate</c> verb: verifies package integrity (block map hashes, coverage, and — when signed —
/// CMS envelope integrity and publisher/subject agreement) and returns a CI-friendly exit code.
/// Runs cross-platform; intended for Linux CI/CD gating.
/// </summary>
internal static class ValidateCommand
{
    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!TryParse(args, out string? path, out bool json, out string? parseError))
        {
            CliContract.WriteError(
                output,
                error,
                json || CliContract.HasJsonFlag(args),
                "msixmgr validate",
                parseError!,
                "Usage: msixmgr validate <package-file-or-directory> [--json]");
            return CliContract.ExitCodes.Usage;
        }

        try
        {
            using MsixPackage package = PackageOpener.Open(path!);
            ValidationReport report = Validate(package);
            if (json)
            {
                output.WriteLine(JsonSerializer.Serialize(report, ReportJsonContext.Default.ValidationReport));
            }
            else
            {
                WriteText(report, output);
            }

            return report.IsValid ? CliContract.ExitCodes.Success : CliContract.ExitCodes.NegativeVerdict;
        }
        catch (Exception ex) when (CliContract.IsOperationalException(ex))
        {
            CliContract.WriteError(output, error, json, "msixmgr validate", ex.Message, null, CliContract.ErrorCode(ex));
            return CliContract.ExitCodes.OperationalError;
        }
    }

    private static bool TryParse(IReadOnlyList<string> args, out string? path, out bool json, out string? error)
    {
        path = null;
        json = false;
        error = null;
        foreach (string arg in args)
        {
            if (arg is "--json")
            {
                json = true;
            }
            else if (arg.StartsWith('-'))
            {
                error = $"unknown option '{arg}'.";
                return false;
            }
            else if (path is null)
            {
                path = arg;
            }
            else
            {
                error = "expected a single package path.";
                return false;
            }
        }

        if (path is null)
        {
            error = "a package path is required.";
            return false;
        }

        return true;
    }

    internal static ValidationReport Validate(MsixPackage package)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        BlockMapVerificationResult blockMap = package.VerifyBlockMap();
        foreach (BlockMapFileResult file in blockMap.Files)
        {
            if (!file.IsValid)
            {
                errors.Add($"block map: '{file.Name}' {file.Error}");
            }
        }

        foreach (string coverage in blockMap.CoverageErrors)
        {
            errors.Add($"block map coverage: {coverage}");
        }

        bool signed = package.IsSigned;
        bool? cmsValid = null;
        bool? bindingValid = null;
        IReadOnlyList<DigestEntryResult>? bindingResults = null;
        if (signed)
        {
            PackageSignature? signature = package.ReadSignature();
            if (signature is null)
            {
                errors.Add("signature: present but could not be read.");
            }
            else
            {
                cmsValid = signature.IsCmsIntegrityValid;
                if (!signature.IsCmsIntegrityValid)
                {
                    errors.Add("signature: CMS envelope integrity check failed.");
                }

                if (!signature.MatchesPublisher(package.Identity.Publisher))
                {
                    errors.Add("signature: signer subject does not match manifest Publisher.");
                }

                // Verify APPX indirect-data digest binding (AXCT, AXBM, AXCI).
                if (signature.IsCmsIntegrityValid && signature.DigestTable is not null)
                {
                    IndirectDataBindingResult binding = package.VerifySignatureBinding(signature);
                    bindingValid = binding.IsBindingValid;
                    bindingResults = binding.Results;

                    if (!binding.IsBindingValid)
                    {
                        errors.Add("signature: APPX indirect-data digest binding FAILED.");
                    }

                    foreach (DigestEntryResult dr in binding.Results)
                    {
                        if (dr.Status == DigestVerificationStatus.Mismatch)
                        {
                            errors.Add($"signature binding: {dr.Tag.ToSpecName()} digest mismatch{(dr.Detail is not null ? $" — {dr.Detail}" : "")}.");
                        }
                        else if (dr.Status == DigestVerificationStatus.PartMissing)
                        {
                            errors.Add($"signature binding: {dr.Tag.ToSpecName()} part missing{(dr.Detail is not null ? $" — {dr.Detail}" : "")}.");
                        }
                        else if (dr.Status == DigestVerificationStatus.DigestMissing)
                        {
                            errors.Add($"signature binding: {dr.Tag.ToSpecName()} unsigned part present{(dr.Detail is not null ? $" — {dr.Detail}" : "")}.");
                        }
                    }
                }
                else if (signature.IsCmsIntegrityValid && signature.DigestTable is null)
                {
                    // CMS is valid but digest table couldn't be parsed.
                    errors.Add($"signature: APPX digest table could not be parsed — {signature.DigestTableError ?? "unknown error"}.");
                    bindingValid = false;
                }

                // Certificate trust chain is still not verified.
                warnings.Add("certificate trust chain is NOT verified; this is not a full authenticity guarantee.");
            }
        }
        else
        {
            warnings.Add("package is unsigned; integrity is self-asserted by its own block map only.");
        }

        // Build the binding detail entries for the report.
        List<BindingDigestReport>? bindingDigests = null;
        if (bindingResults is not null)
        {
            bindingDigests = new List<BindingDigestReport>(bindingResults.Count);
            foreach (DigestEntryResult dr in bindingResults)
            {
                bindingDigests.Add(new BindingDigestReport
                {
                    Tag = dr.Tag.ToSpecName(),
                    Status = dr.Status.ToString(),
                    Detail = dr.Detail,
                });
            }
        }

        return new ValidationReport
        {
            PackageFullName = package.Identity.PackageFullName,
            IsValid = errors.Count == 0,
            BlockMapValid = blockMap.IsValid,
            VerifiedFileCount = blockMap.Files.Count,
            IsSigned = signed,
            CmsIntegrityValid = cmsValid,
            SignatureBindingVerified = bindingValid,
            SignatureTrustVerified = signed ? false : null,
            BindingDigests = bindingDigests,
            Errors = errors,
            Warnings = warnings,
        };
    }

    private static void WriteText(ValidationReport r, TextWriter o)
    {
        // The verdict is scoped to integrity, not authenticity — say so plainly.
        o.WriteLine(r.IsValid ? $"INTEGRITY OK      {r.PackageFullName}" : $"INTEGRITY FAILED  {r.PackageFullName}");
        string fileCount = r.VerifiedFileCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        o.WriteLine($"  Block map : {(r.BlockMapValid ? "ok" : "FAILED")} ({fileCount} files)");
        string signature;
        if (!r.IsSigned)
        {
            signature = "unsigned";
        }
        else if (r.CmsIntegrityValid != true)
        {
            signature = "CMS envelope FAILED";
        }
        else if (r.SignatureBindingVerified == true)
        {
            // Derive the verified/not-verified tag lists from actual results.
            var verified = new List<string>();
            var notVerified = new List<string>();
            if (r.BindingDigests is not null)
            {
                foreach (BindingDigestReport d in r.BindingDigests)
                {
                    if (string.Equals(d.Status, "Valid", StringComparison.OrdinalIgnoreCase))
                    {
                        verified.Add(d.Tag);
                    }
                    else if (string.Equals(d.Status, "NotVerified", StringComparison.OrdinalIgnoreCase))
                    {
                        notVerified.Add(d.Tag);
                    }
                }
            }

            string verifiedStr = verified.Count > 0 ? string.Join("/", verified) : "none";
            string notVerifiedStr = notVerified.Count > 0 ? $"; {string.Join("/", notVerified)} not verified" : "";
            signature = $"CMS envelope ok, binding verified ({verifiedStr}{notVerifiedStr}; trust NOT verified)";
        }
        else if (r.SignatureBindingVerified == false)
        {
            signature = "CMS envelope ok, binding FAILED (trust NOT verified)";
        }
        else
        {
            signature = "CMS envelope ok (binding + trust NOT verified)";
        }

        o.WriteLine($"  Signature : {signature}");

        if (r.BindingDigests is not null)
        {
            foreach (BindingDigestReport d in r.BindingDigests)
            {
                string status = d.Status;
                string detail = d.Detail is not null ? $" — {d.Detail}" : "";
                o.WriteLine($"    {d.Tag,-4} : {status}{detail}");
            }
        }

        foreach (string err in r.Errors)
        {
            o.WriteLine($"  error: {err}");
        }

        foreach (string warn in r.Warnings)
        {
            o.WriteLine($"  note:  {warn}");
        }
    }
}
