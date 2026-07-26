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
    /// <summary>The footprint parts that binding verification may need to read.</summary>
    private static readonly (AppxDigestTag Tag, string PartName)[] VerifiableParts =
    [
        (AppxDigestTag.Axct, OpcPartNames.ContentTypes),
        (AppxDigestTag.Axbm, OpcPartNames.AppxBlockMap),
        (AppxDigestTag.Axci, OpcPartNames.CodeIntegrityCatalog),
    ];

    /// <summary>
    /// Snapshots the footprint parts from <paramref name="opc"/> once, then verifies the
    /// digest table against those snapshots. This ensures exactly one read per part within
    /// a single call, but does <em>not</em> share state with earlier parsing/verification
    /// phases — use <see cref="VerifyFromSnapshots"/> from <see cref="MsixPackage.VerifySignatureBinding"/>
    /// for the full single-read-single-hash guarantee across the pipeline.
    /// </summary>
    /// <param name="table">The parsed digest table from the CMS signature.</param>
    /// <param name="opc">The OPC package to verify against.</param>
    /// <returns>A structured result describing the verification outcome for each tag.</returns>
    internal static IndirectDataBindingResult Verify(AppxDigestTable table, IOpcPackage opc)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(opc);

        // Single-read snapshot: read each verifiable part exactly once.
        var snapshots = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, partName) in VerifiableParts)
        {
            if (opc.ContainsPart(partName))
            {
                snapshots[partName] = ReadPartBytes(opc, partName);
            }
        }

        return VerifyFromSnapshots(table, snapshots);
    }

    /// <summary>
    /// Verifies the digest table against pre-read part byte snapshots. This overload
    /// is the core verification path — callers that already hold part bytes (or want to
    /// control when reads happen) should use this directly.
    /// </summary>
    /// <param name="table">The parsed digest table from the CMS signature.</param>
    /// <param name="partSnapshots">
    /// A dictionary mapping OPC part names (e.g. <c>[Content_Types].xml</c>) to their exact
    /// decompressed bytes. Parts absent from the dictionary are treated as missing.
    /// </param>
    public static IndirectDataBindingResult VerifyFromSnapshots(
        AppxDigestTable table,
        IReadOnlyDictionary<string, byte[]> partSnapshots)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(partSnapshots);

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
                    Detail = $"{entry.Tag.ToSpecName()} verification is not supported — exact ZIP byte ranges are not recoverable from the public specification.",
                },
                AppxDigestTag.Axct => VerifySnapshot(entry, OpcPartNames.ContentTypes, partSnapshots, ref allVerifiableOk),
                AppxDigestTag.Axbm => VerifySnapshot(entry, OpcPartNames.AppxBlockMap, partSnapshots, ref allVerifiableOk),
                AppxDigestTag.Axci => VerifySnapshot(entry, OpcPartNames.CodeIntegrityCatalog, partSnapshots, ref allVerifiableOk),
                _ => new DigestEntryResult
                {
                    Tag = entry.Tag,
                    Status = DigestVerificationStatus.Mismatch,
                    Detail = $"Unknown tag {entry.Tag.ToSpecName()}.",
                },
            };

            results.Add(result);
        }

        // Check for AXCI tag-vs-part consistency: if the part exists but no AXCI entry, that is
        // a security-relevant failure — an attacker could add an unsigned CodeIntegrity.cat and
        // we would silently accept it alongside a "binding verified" verdict.
        AppxDigestEntry? axciEntry = table.FindEntry(AppxDigestTag.Axci);
        if (axciEntry is null && partSnapshots.ContainsKey(OpcPartNames.CodeIntegrityCatalog))
        {
            allVerifiableOk = false;
            results.Add(new DigestEntryResult
            {
                Tag = AppxDigestTag.Axci,
                Status = DigestVerificationStatus.DigestMissing,
                Detail = "CodeIntegrity.cat exists in the package but no AXCI digest is present in the signature — the catalog is unsigned and untrusted.",
            });
        }

        string summary = allVerifiableOk
            ? "APPX indirect-data binding verified for AXCT, AXBM" +
              (axciEntry is not null ? ", AXCI" : "") +
              "; AXPC and AXCD are present but not verified (byte ranges unrecoverable)."
            : "APPX indirect-data binding FAILED — one or more verifiable digests do not match or are missing.";

        return new IndirectDataBindingResult
        {
            IsBindingValid = allVerifiableOk,
            Results = results,
            Summary = summary,
        };
    }

    private static DigestEntryResult VerifySnapshot(
        AppxDigestEntry entry,
        string partName,
        IReadOnlyDictionary<string, byte[]> snapshots,
        ref bool allOk)
    {
        if (!snapshots.TryGetValue(partName, out byte[]? partBytes))
        {
            allOk = false;
            return new DigestEntryResult
            {
                Tag = entry.Tag,
                Status = DigestVerificationStatus.PartMissing,
                Detail = $"Part '{partName}' not found in the package.",
            };
        }

        byte[] actual = SHA256.HashData(partBytes);
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

    private static byte[] ReadPartBytes(IOpcPackage opc, string partName)
    {
        using Stream stream = opc.OpenPart(partName);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
