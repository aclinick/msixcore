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
            error.WriteLine($"msixmgr validate: {parseError}");
            error.WriteLine("Usage: msixmgr validate <package-file-or-directory> [--json]");
            return 2;
        }

        try
        {
            using MsixPackage package = PackageOpener.Open(path!);
            ValidationReport report = Validate(package);
            if (json)
            {
                output.WriteLine(JsonSerializer.Serialize(report, ReportJson.Options));
            }
            else
            {
                WriteText(report, output);
            }

            return report.IsValid ? 0 : 1;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            error.WriteLine($"msixmgr validate: {ex.Message}");
            return 1;
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

    private static ValidationReport Validate(MsixPackage package)
    {
        var errors = new List<string>();

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
            Errors = errors,
        };
    }

    private static void WriteText(ValidationReport r, TextWriter o)
    {
        o.WriteLine(r.IsValid ? $"VALID    {r.PackageFullName}" : $"INVALID  {r.PackageFullName}");
        string fileCount = r.VerifiedFileCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        o.WriteLine($"  Block map : {(r.BlockMapValid ? "ok" : "FAILED")} ({fileCount} files)");
        string signature = !r.IsSigned
            ? "unsigned"
            : r.CmsIntegrityValid == true ? "CMS integrity ok" : "CMS integrity FAILED";
        o.WriteLine($"  Signature : {signature}");
        foreach (string err in r.Errors)
        {
            o.WriteLine($"  - {err}");
        }
    }
}
