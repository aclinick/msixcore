using System.Security.Cryptography;
using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging.Integrity;

/// <summary>
/// Verifies that an <see cref="AppxDigestTable"/> (from the CMS indirect-data content)
/// binds to the actual package contents by comparing SHA-256 digests of the decompressed
/// footprint parts.
/// </summary>
public static class AppxDigestTableVerifier
{
    /// <summary>
    /// Verifies the digest table against the package contents.
    /// </summary>
    /// <param name="table">The parsed digest table from the CMS signature.</param>
    /// <param name="opc">The OPC package to verify against.</param>
    /// <returns>A structured result describing the verification outcome for each tag.</returns>
    public static IndirectDataBindingResult Verify(AppxDigestTable table, IOpcPackage opc)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(opc);

        var results = new List<DigestEntryResult>();
        bool allVerifiableOk = true;

        foreach (AppxDigestEntry entry in table.Entries)
        {
            DigestEntryResult result = entry.Tag switch
            {
                AppxDigestTag.Axpc or AppxDigestTag.Axcd => new DigestEntryResult
                {
                    Tag = entry.Tag,
                    Status = DigestVerificationStatus.NotVerified,
                    Detail = $"{entry.Tag} verification is not supported — exact ZIP byte ranges are not recoverable from the public specification.",
                },
                AppxDigestTag.Axct => VerifyPart(entry, OpcPartNames.ContentTypes, opc, ref allVerifiableOk),
                AppxDigestTag.Axbm => VerifyPart(entry, OpcPartNames.AppxBlockMap, opc, ref allVerifiableOk),
                AppxDigestTag.Axci => VerifyPart(entry, OpcPartNames.CodeIntegrityCatalog, opc, ref allVerifiableOk),
                _ => new DigestEntryResult
                {
                    Tag = entry.Tag,
                    Status = DigestVerificationStatus.Mismatch,
                    Detail = $"Unknown tag {entry.Tag}.",
                },
            };

            results.Add(result);
        }

        // Check for AXCI tag-vs-part consistency: if the part exists but no AXCI entry, flag it.
        // (The table parser already ensured no unknown tags, so we only need to check the reverse.)
        AppxDigestEntry? axciEntry = table.FindEntry(AppxDigestTag.Axci);
        if (axciEntry is null && opc.ContainsPart(OpcPartNames.CodeIntegrityCatalog))
        {
            // Part present without a digest entry — not a verification failure per spec (AXCI is optional),
            // but surface it for visibility.
            results.Add(new DigestEntryResult
            {
                Tag = AppxDigestTag.Axci,
                Status = DigestVerificationStatus.DigestMissing,
                Detail = "CodeIntegrity.cat exists in the package but no AXCI digest is present in the signature.",
            });
        }

        string summary = allVerifiableOk
            ? "APPX indirect-data binding verified for AXCT, AXBM" +
              (axciEntry is not null ? ", AXCI" : "") +
              "; AXPC and AXCD are present but not verified (byte ranges unrecoverable)."
            : "APPX indirect-data binding FAILED — one or more verifiable digests do not match.";

        return new IndirectDataBindingResult
        {
            IsBindingValid = allVerifiableOk,
            Results = results,
            Summary = summary,
        };
    }

    private static DigestEntryResult VerifyPart(
        AppxDigestEntry entry,
        string partName,
        IOpcPackage opc,
        ref bool allOk)
    {
        if (!opc.ContainsPart(partName))
        {
            allOk = false;
            return new DigestEntryResult
            {
                Tag = entry.Tag,
                Status = DigestVerificationStatus.PartMissing,
                Detail = $"Part '{partName}' not found in the package.",
            };
        }

        byte[] actual;
        using (Stream stream = opc.OpenPart(partName))
        using (var ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            actual = SHA256.HashData(ms.ToArray());
        }

        bool match = CryptographicOperations.FixedTimeEquals(actual, entry.Digest.Span);
        if (!match)
        {
            allOk = false;
        }

        return new DigestEntryResult
        {
            Tag = entry.Tag,
            Status = match ? DigestVerificationStatus.Valid : DigestVerificationStatus.Mismatch,
            Detail = match ? null : $"SHA-256 digest mismatch for '{partName}'.",
        };
    }
}
