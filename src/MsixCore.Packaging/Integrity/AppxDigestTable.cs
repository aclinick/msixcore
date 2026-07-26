using System.Buffers.Binary;
using System.Formats.Asn1;
using System.Security.Cryptography.Pkcs;

namespace MsixCore.Packaging.Integrity;

/// <summary>
/// Parses the APPX digest table from the CMS <c>SpcIndirectDataContent</c> carried inside
/// an <c>AppxSignature.p7x</c>. The table binds the CMS signature to specific package
/// components via SHA-256 digests.
/// </summary>
public sealed class AppxDigestTable
{
    /// <summary>OID for <c>SPC_INDIRECT_DATA_CONTENT</c> — the required CMS inner content type.</summary>
    public const string SpcIndirectDataContentOid = "1.3.6.1.4.1.311.2.1.4";

    /// <summary>The 4-byte ASCII header that begins the digest OCTET STRING.</summary>
    private static ReadOnlySpan<byte> AppxHeader => "APPX"u8;

    private const int DigestSize = 32; // SHA-256
    private const int EntrySize = 4 + DigestSize; // tag (4) + digest (32)

    /// <summary>The parsed digest entries in table order.</summary>
    public IReadOnlyList<AppxDigestEntry> Entries { get; }

    private AppxDigestTable(IReadOnlyList<AppxDigestEntry> entries)
    {
        Entries = entries;
    }

    /// <summary>
    /// Parses the APPX digest table from a <see cref="SignedCms"/> instance.
    /// The CMS content type must be <see cref="SpcIndirectDataContentOid"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">The content type is wrong, or the table is malformed.</exception>
    public static AppxDigestTable Parse(SignedCms cms)
    {
        ArgumentNullException.ThrowIfNull(cms);

        // Validate the CMS inner content type.
        string contentType = cms.ContentInfo.ContentType.Value
            ?? throw new InvalidDataException("The CMS content type OID is null.");

        if (!string.Equals(contentType, SpcIndirectDataContentOid, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unexpected CMS content type '{contentType}'; expected SPC_INDIRECT_DATA_CONTENT ({SpcIndirectDataContentOid}).");
        }

        byte[] content = cms.ContentInfo.Content;
        if (content.Length == 0)
        {
            throw new InvalidDataException("The CMS content (SpcIndirectDataContent) is empty.");
        }

        // Parse the ASN.1 SpcIndirectDataContent to extract the DigestInfo.digest OCTET STRING.
        byte[] digestTableBytes = ParseIndirectDataContent(content);

        return ParseDigestTable(digestTableBytes);
    }

    /// <summary>
    /// Parses <c>SpcIndirectDataContent</c> ASN.1 and returns the <c>DigestInfo.digest</c>
    /// OCTET STRING (the raw APPX digest table bytes).
    /// </summary>
    private static byte[] ParseIndirectDataContent(byte[] content)
    {
        // SpcIndirectDataContent ::= SEQUENCE {
        //     data          SpcAttributeTypeAndOptionalValue,
        //     messageDigest DigestInfo
        // }
        try
        {
            var reader = new AsnReader(content, AsnEncodingRules.DER);
            AsnReader seq = reader.ReadSequence();

            // data: SpcAttributeTypeAndOptionalValue — skip it entirely.
            seq.ReadSequence(); // consume and discard

            // messageDigest: DigestInfo ::= SEQUENCE { digestAlgorithm AlgorithmIdentifier, digest OCTET STRING }
            AsnReader digestInfo = seq.ReadSequence();

            // digestAlgorithm: AlgorithmIdentifier ::= SEQUENCE { algorithm OID, parameters ANY OPTIONAL }
            AsnReader algId = digestInfo.ReadSequence();
            string algOid = algId.ReadObjectIdentifier();

            // SHA-256 OID = 2.16.840.1.101.3.4.2.1
            if (!string.Equals(algOid, "2.16.840.1.101.3.4.2.1", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Unsupported digest algorithm OID '{algOid}'; only SHA-256 (2.16.840.1.101.3.4.2.1) is supported.");
            }

            // Parameters may be absent or explicit DER NULL — handle both.
            if (algId.HasData)
            {
                algId.ReadNull();
            }

            // digest OCTET STRING
            byte[] digestTable = digestInfo.ReadOctetString();

            return digestTable;
        }
        catch (AsnContentException ex)
        {
            throw new InvalidDataException("Failed to parse SpcIndirectDataContent ASN.1 structure.", ex);
        }
    }

    /// <summary>Parses the raw APPX digest table bytes into entries.</summary>
    private static AppxDigestTable ParseDigestTable(byte[] tableBytes)
    {
        if (tableBytes.Length < AppxHeader.Length)
        {
            throw new InvalidDataException(
                $"APPX digest table is too short ({tableBytes.Length} bytes); expected at least {AppxHeader.Length} bytes for header.");
        }

        // Validate the "APPX" header.
        if (!tableBytes.AsSpan(0, AppxHeader.Length).SequenceEqual(AppxHeader))
        {
            throw new InvalidDataException("APPX digest table does not start with the 'APPX' header.");
        }

        int remaining = tableBytes.Length - AppxHeader.Length;
        if (remaining % EntrySize != 0)
        {
            throw new InvalidDataException(
                $"APPX digest table body length ({remaining}) is not a multiple of entry size ({EntrySize}).");
        }

        int entryCount = remaining / EntrySize;

        // Valid table: 4 mandatory entries, or 4 mandatory + 1 optional AXCI = 5.
        // Total lengths: 4 + 4*36 = 148, or 4 + 5*36 = 184.
        if (entryCount is not (4 or 5))
        {
            throw new InvalidDataException(
                $"APPX digest table has {entryCount} entries; expected 4 or 5 (total length must be 148 or 184 bytes, got {tableBytes.Length}).");
        }

        var entries = new List<AppxDigestEntry>(entryCount);
        var seenTags = new HashSet<AppxDigestTag>();
        int offset = AppxHeader.Length;

        for (int i = 0; i < entryCount; i++)
        {
            uint rawTag = BinaryPrimitives.ReadUInt32LittleEndian(tableBytes.AsSpan(offset));
            if (!Enum.IsDefined((AppxDigestTag)rawTag))
            {
                throw new InvalidDataException(
                    $"Unknown APPX digest tag 0x{rawTag:X8} at offset {offset}.");
            }

            var tag = (AppxDigestTag)rawTag;
            if (!seenTags.Add(tag))
            {
                throw new InvalidDataException($"Duplicate APPX digest tag '{tag}' at offset {offset}.");
            }

            byte[] digest = new byte[DigestSize];
            tableBytes.AsSpan(offset + 4, DigestSize).CopyTo(digest);

            entries.Add(new AppxDigestEntry { Tag = tag, Digest = digest });
            offset += EntrySize;
        }

        // Validate mandatory tags.
        AppxDigestTag[] mandatory = [AppxDigestTag.Axpc, AppxDigestTag.Axcd, AppxDigestTag.Axct, AppxDigestTag.Axbm];
        foreach (AppxDigestTag required in mandatory)
        {
            if (!seenTags.Contains(required))
            {
                throw new InvalidDataException($"APPX digest table is missing mandatory tag '{required}'.");
            }
        }

        // The only optional tag allowed is AXCI; others were already rejected as unknown.
        return new AppxDigestTable(entries);
    }

    /// <summary>Returns the entry for the given tag, or <see langword="null"/> if not present.</summary>
    public AppxDigestEntry? FindEntry(AppxDigestTag tag)
    {
        foreach (AppxDigestEntry entry in Entries)
        {
            if (entry.Tag == tag)
            {
                return entry;
            }
        }

        return null;
    }
}
