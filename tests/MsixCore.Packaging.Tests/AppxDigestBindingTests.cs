using System.Buffers.Binary;
using System.Formats.Asn1;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using MsixCore.Packaging.Integrity;
using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging.Tests;

/// <summary>
/// Tests for the APPX indirect-data digest binding implementation:
/// <see cref="AppxDigestTable"/>, <see cref="AppxDigestTableVerifier"/>,
/// and the integration through <see cref="PackageSignatureReader"/> and <see cref="MsixPackage"/>.
/// </summary>
public class AppxDigestBindingTests
{
    private static readonly byte[] P7xMagic = "PKCX"u8.ToArray();
    private const string SpcIndirectDataOid = "1.3.6.1.4.1.311.2.1.4";
    private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";

    #region Helpers

    private static X509Certificate2 CreateCert(string subject = "CN=Test Publisher")
    {
        using RSA rsa = RSA.Create(2048);
        var req = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    /// <summary>
    /// Builds the raw APPX digest table bytes: "APPX" header + N entries of (tag LE32 + 32-byte SHA-256).
    /// </summary>
    private static byte[] BuildDigestTableBytes(params (AppxDigestTag tag, byte[] digest)[] entries)
    {
        int len = 4 + entries.Length * 36; // "APPX" + N * (4 + 32)
        byte[] table = new byte[len];
        "APPX"u8.CopyTo(table);
        int offset = 4;
        foreach (var (tag, digest) in entries)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(offset), (uint)tag);
            digest.AsSpan(0, 32).CopyTo(table.AsSpan(offset + 4));
            offset += 36;
        }

        return table;
    }

    /// <summary>Wraps a raw APPX digest table in ASN.1 SpcIndirectDataContent.</summary>
    private static byte[] BuildSpcIndirectDataContent(byte[] digestTableBytes, bool includeAlgNull = true, string? algOid = null)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence()) // SpcIndirectDataContent
        {
            // data: SpcAttributeTypeAndOptionalValue (minimal — just type OID, no value)
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier("1.3.6.1.4.1.311.2.1.30"); // SPC_PE_IMAGE_DATA
            }

            // messageDigest: DigestInfo
            using (writer.PushSequence())
            {
                // AlgorithmIdentifier
                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier(algOid ?? Sha256Oid);
                    if (includeAlgNull)
                    {
                        writer.WriteNull();
                    }
                }

                writer.WriteOctetString(digestTableBytes);
            }
        }

        return writer.Encode();
    }

    /// <summary>
    /// Builds a complete signed AppxSignature.p7x (PKCX magic + CMS) with the given
    /// SpcIndirectDataContent carrying the supplied digest table entries.
    /// </summary>
    private static byte[] BuildP7x(
        X509Certificate2 cert,
        (AppxDigestTag tag, byte[] digest)[] entries,
        bool includeAlgNull = true)
    {
        byte[] tableBytes = BuildDigestTableBytes(entries);
        byte[] spcContent = BuildSpcIndirectDataContent(tableBytes, includeAlgNull);
        return BuildP7xFromSpcContent(cert, spcContent);
    }

    private static byte[] BuildP7xFromSpcContent(X509Certificate2 cert, byte[] spcContent)
    {
        var contentInfo = new ContentInfo(new Oid(SpcIndirectDataOid), spcContent);
        var cms = new SignedCms(contentInfo, detached: false);
        var signer = new CmsSigner(cert) { IncludeOption = X509IncludeOption.EndCertOnly };
        cms.ComputeSignature(signer);

        byte[] der = cms.Encode();
        byte[] p7x = new byte[P7xMagic.Length + der.Length];
        P7xMagic.CopyTo(p7x, 0);
        der.CopyTo(p7x, P7xMagic.Length);
        return p7x;
    }

    private static byte[] HashOf(byte[] data) => SHA256.HashData(data);

    private static byte[] RandomDigest()
    {
        byte[] d = new byte[32];
        RandomNumberGenerator.Fill(d);
        return d;
    }

    /// <summary>Builds a minimal OPC ZIP with the given parts (for verifier tests).</summary>
    private static OpcPackage BuildOpc(Dictionary<string, byte[]> parts)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in parts)
            {
                using Stream entry = archive.CreateEntry(name).Open();
                entry.Write(content);
            }
        }

        stream.Position = 0;
        return OpcPackage.Open(stream, leaveOpen: false);
    }

    /// <summary>Standard 4 mandatory entries with correct digests for the given part contents.</summary>
    private static (AppxDigestTag tag, byte[] digest)[] MakeCorrectEntries(
        byte[] contentTypesXml, byte[] blockMapXml, byte[]? codeIntegrityCat = null)
    {
        var entries = new List<(AppxDigestTag, byte[])>
        {
            (AppxDigestTag.Axpc, RandomDigest()),
            (AppxDigestTag.Axcd, RandomDigest()),
            (AppxDigestTag.Axct, HashOf(contentTypesXml)),
            (AppxDigestTag.Axbm, HashOf(blockMapXml)),
        };
        if (codeIntegrityCat is not null)
        {
            entries.Add((AppxDigestTag.Axci, HashOf(codeIntegrityCat)));
        }

        return entries.ToArray();
    }

    #endregion

    #region AppxDigestTable parsing

    [Fact]
    public void Parse_ValidTable_4Entries_Succeeds()
    {
        using X509Certificate2 cert = CreateCert();
        var entries = new[]
        {
            (AppxDigestTag.Axpc, RandomDigest()),
            (AppxDigestTag.Axcd, RandomDigest()),
            (AppxDigestTag.Axct, RandomDigest()),
            (AppxDigestTag.Axbm, RandomDigest()),
        };
        byte[] p7x = BuildP7x(cert, entries);

        PackageSignature sig = PackageSignatureReader.Read(p7x);

        Assert.NotNull(sig.DigestTable);
        Assert.Equal(4, sig.DigestTable!.Entries.Count);
        Assert.Null(sig.DigestTableError);
    }

    [Fact]
    public void Parse_ValidTable_5Entries_WithAxci_Succeeds()
    {
        using X509Certificate2 cert = CreateCert();
        var entries = new[]
        {
            (AppxDigestTag.Axpc, RandomDigest()),
            (AppxDigestTag.Axcd, RandomDigest()),
            (AppxDigestTag.Axct, RandomDigest()),
            (AppxDigestTag.Axbm, RandomDigest()),
            (AppxDigestTag.Axci, RandomDigest()),
        };
        byte[] p7x = BuildP7x(cert, entries);

        PackageSignature sig = PackageSignatureReader.Read(p7x);

        Assert.NotNull(sig.DigestTable);
        Assert.Equal(5, sig.DigestTable!.Entries.Count);
    }

    [Fact]
    public void Parse_AlgorithmIdentifier_WithoutNull_Succeeds()
    {
        using X509Certificate2 cert = CreateCert();
        var entries = new[]
        {
            (AppxDigestTag.Axpc, RandomDigest()),
            (AppxDigestTag.Axcd, RandomDigest()),
            (AppxDigestTag.Axct, RandomDigest()),
            (AppxDigestTag.Axbm, RandomDigest()),
        };
        byte[] p7x = BuildP7x(cert, entries, includeAlgNull: false);

        PackageSignature sig = PackageSignatureReader.Read(p7x);

        Assert.NotNull(sig.DigestTable);
        Assert.Equal(4, sig.DigestTable!.Entries.Count);
    }

    [Fact]
    public void Parse_WrongContentTypeOid_FailsWithError()
    {
        using X509Certificate2 cert = CreateCert();
        // Build CMS with a wrong content type OID.
        byte[] tableBytes = BuildDigestTableBytes(
            (AppxDigestTag.Axpc, RandomDigest()),
            (AppxDigestTag.Axcd, RandomDigest()),
            (AppxDigestTag.Axct, RandomDigest()),
            (AppxDigestTag.Axbm, RandomDigest()));
        byte[] spcContent = BuildSpcIndirectDataContent(tableBytes);

        // Use a generic data OID instead of the SPC OID.
        var contentInfo = new ContentInfo(new Oid("1.2.840.113549.1.7.1"), spcContent);
        var cms = new SignedCms(contentInfo, detached: false);
        var signer = new CmsSigner(cert) { IncludeOption = X509IncludeOption.EndCertOnly };
        cms.ComputeSignature(signer);

        byte[] der = cms.Encode();
        byte[] p7x = new byte[P7xMagic.Length + der.Length];
        P7xMagic.CopyTo(p7x, 0);
        der.CopyTo(p7x, P7xMagic.Length);

        PackageSignature sig = PackageSignatureReader.Read(p7x);

        Assert.True(sig.IsCmsIntegrityValid);
        Assert.Null(sig.DigestTable);
        Assert.Contains("Unexpected CMS content type", sig.DigestTableError);
    }

    [Fact]
    public void Parse_BadAppxHeader_FailsWithError()
    {
        using X509Certificate2 cert = CreateCert();
        // Build table with wrong header.
        byte[] tableBytes = new byte[148];
        "XXXX"u8.CopyTo(tableBytes);
        // Fill mandatory tags at correct offsets (won't matter, header check fails first)
        byte[] spcContent = BuildSpcIndirectDataContent(tableBytes);
        byte[] p7x = BuildP7xFromSpcContent(cert, spcContent);

        PackageSignature sig = PackageSignatureReader.Read(p7x);

        Assert.Null(sig.DigestTable);
        Assert.Contains("APPX", sig.DigestTableError!);
    }

    [Fact]
    public void Parse_WrongTableLength_FailsWithError()
    {
        using X509Certificate2 cert = CreateCert();
        // 100 bytes is not 148 or 184.
        byte[] tableBytes = new byte[100];
        "APPX"u8.CopyTo(tableBytes);
        byte[] spcContent = BuildSpcIndirectDataContent(tableBytes);
        byte[] p7x = BuildP7xFromSpcContent(cert, spcContent);

        PackageSignature sig = PackageSignatureReader.Read(p7x);

        Assert.Null(sig.DigestTable);
        Assert.NotNull(sig.DigestTableError);
    }

    [Fact]
    public void Parse_UnknownTag_FailsWithError()
    {
        using X509Certificate2 cert = CreateCert();
        // Build 4-entry table but replace one tag with unknown.
        byte[] tableBytes = BuildDigestTableBytes(
            (AppxDigestTag.Axpc, RandomDigest()),
            (AppxDigestTag.Axcd, RandomDigest()),
            (AppxDigestTag.Axct, RandomDigest()),
            (AppxDigestTag.Axbm, RandomDigest()));
        // Overwrite AXBM tag with unknown value.
        BinaryPrimitives.WriteUInt32LittleEndian(tableBytes.AsSpan(4 + 3 * 36), 0xDEADBEEF);

        byte[] spcContent = BuildSpcIndirectDataContent(tableBytes);
        byte[] p7x = BuildP7xFromSpcContent(cert, spcContent);

        PackageSignature sig = PackageSignatureReader.Read(p7x);

        Assert.Null(sig.DigestTable);
        Assert.Contains("Unknown", sig.DigestTableError!);
    }

    [Fact]
    public void Parse_DuplicateTag_FailsWithError()
    {
        using X509Certificate2 cert = CreateCert();
        byte[] tableBytes = BuildDigestTableBytes(
            (AppxDigestTag.Axpc, RandomDigest()),
            (AppxDigestTag.Axcd, RandomDigest()),
            (AppxDigestTag.Axct, RandomDigest()),
            (AppxDigestTag.Axct, RandomDigest())); // duplicate AXCT

        byte[] spcContent = BuildSpcIndirectDataContent(tableBytes);
        byte[] p7x = BuildP7xFromSpcContent(cert, spcContent);

        PackageSignature sig = PackageSignatureReader.Read(p7x);

        Assert.Null(sig.DigestTable);
        Assert.Contains("Duplicate", sig.DigestTableError!);
    }

    [Fact]
    public void Parse_MissingMandatoryTag_FailsWithError()
    {
        using X509Certificate2 cert = CreateCert();
        // 4 entries but missing AXBM, replaced with AXCI.
        byte[] tableBytes = BuildDigestTableBytes(
            (AppxDigestTag.Axpc, RandomDigest()),
            (AppxDigestTag.Axcd, RandomDigest()),
            (AppxDigestTag.Axct, RandomDigest()),
            (AppxDigestTag.Axci, RandomDigest())); // AXCI instead of AXBM

        byte[] spcContent = BuildSpcIndirectDataContent(tableBytes);
        byte[] p7x = BuildP7xFromSpcContent(cert, spcContent);

        PackageSignature sig = PackageSignatureReader.Read(p7x);

        Assert.Null(sig.DigestTable);
        Assert.Contains("Axbm", sig.DigestTableError!);
    }

    [Fact]
    public void Parse_TruncatedOctetString_FailsWithError()
    {
        using X509Certificate2 cert = CreateCert();
        // Only 10 bytes — too short for even the header + 1 entry.
        byte[] tableBytes = new byte[10];
        "APPX"u8.CopyTo(tableBytes);

        byte[] spcContent = BuildSpcIndirectDataContent(tableBytes);
        byte[] p7x = BuildP7xFromSpcContent(cert, spcContent);

        PackageSignature sig = PackageSignatureReader.Read(p7x);

        Assert.Null(sig.DigestTable);
        Assert.NotNull(sig.DigestTableError);
    }

    [Fact]
    public void Parse_UnsupportedDigestAlgorithm_FailsWithError()
    {
        using X509Certificate2 cert = CreateCert();
        byte[] tableBytes = BuildDigestTableBytes(
            (AppxDigestTag.Axpc, RandomDigest()),
            (AppxDigestTag.Axcd, RandomDigest()),
            (AppxDigestTag.Axct, RandomDigest()),
            (AppxDigestTag.Axbm, RandomDigest()));

        // Use SHA-384 OID instead of SHA-256.
        byte[] spcContent = BuildSpcIndirectDataContent(tableBytes, algOid: "2.16.840.1.101.3.4.2.2");
        byte[] p7x = BuildP7xFromSpcContent(cert, spcContent);

        PackageSignature sig = PackageSignatureReader.Read(p7x);

        Assert.Null(sig.DigestTable);
        Assert.Contains("Unsupported digest algorithm", sig.DigestTableError!);
    }

    #endregion

    #region Digest verification — individual parts

    [Fact]
    public void Verify_AllPartsMatch_IsValid()
    {
        byte[] contentTypes = "<Types />"u8.ToArray();
        byte[] blockMap = "<BlockMap />"u8.ToArray();

        var entries = MakeCorrectEntries(contentTypes, blockMap);
        var parts = new Dictionary<string, byte[]>
        {
            ["[Content_Types].xml"] = contentTypes,
            ["AppxBlockMap.xml"] = blockMap,
        };

        using OpcPackage opc = BuildOpc(parts);
        AppxDigestTable table = ParseTableDirect(entries);

        IndirectDataBindingResult result = AppxDigestTableVerifier.Verify(table, opc);

        Assert.True(result.IsBindingValid);
        AssertTagStatus(result, AppxDigestTag.Axct, DigestVerificationStatus.Valid);
        AssertTagStatus(result, AppxDigestTag.Axbm, DigestVerificationStatus.Valid);
        AssertTagStatus(result, AppxDigestTag.Axpc, DigestVerificationStatus.NotVerified);
        AssertTagStatus(result, AppxDigestTag.Axcd, DigestVerificationStatus.NotVerified);
    }

    [Fact]
    public void Verify_AxctMismatch_Fails()
    {
        byte[] contentTypes = "<Types />"u8.ToArray();
        byte[] blockMap = "<BlockMap />"u8.ToArray();

        var entries = MakeCorrectEntries(contentTypes, blockMap);
        // Tamper: put wrong content for [Content_Types].xml in the package.
        var parts = new Dictionary<string, byte[]>
        {
            ["[Content_Types].xml"] = "<Types><Default Extension='xml' ContentType='text/xml'/></Types>"u8.ToArray(),
            ["AppxBlockMap.xml"] = blockMap,
        };

        using OpcPackage opc = BuildOpc(parts);
        AppxDigestTable table = ParseTableDirect(entries);

        IndirectDataBindingResult result = AppxDigestTableVerifier.Verify(table, opc);

        Assert.False(result.IsBindingValid);
        AssertTagStatus(result, AppxDigestTag.Axct, DigestVerificationStatus.Mismatch);
        AssertTagStatus(result, AppxDigestTag.Axbm, DigestVerificationStatus.Valid);
    }

    [Fact]
    public void Verify_AxbmMismatch_Fails()
    {
        byte[] contentTypes = "<Types />"u8.ToArray();
        byte[] blockMap = "<BlockMap />"u8.ToArray();

        var entries = MakeCorrectEntries(contentTypes, blockMap);
        // Tamper: put wrong content for AppxBlockMap.xml in the package.
        var parts = new Dictionary<string, byte[]>
        {
            ["[Content_Types].xml"] = contentTypes,
            ["AppxBlockMap.xml"] = "<BlockMap><File Name='evil.exe' /></BlockMap>"u8.ToArray(),
        };

        using OpcPackage opc = BuildOpc(parts);
        AppxDigestTable table = ParseTableDirect(entries);

        IndirectDataBindingResult result = AppxDigestTableVerifier.Verify(table, opc);

        Assert.False(result.IsBindingValid);
        AssertTagStatus(result, AppxDigestTag.Axbm, DigestVerificationStatus.Mismatch);
        AssertTagStatus(result, AppxDigestTag.Axct, DigestVerificationStatus.Valid);
    }

    [Fact]
    public void Verify_AxciPresent_AndMatching_IsValid()
    {
        byte[] contentTypes = "<Types />"u8.ToArray();
        byte[] blockMap = "<BlockMap />"u8.ToArray();
        byte[] codeIntegrity = new byte[] { 0xCA, 0xFE };

        var entries = MakeCorrectEntries(contentTypes, blockMap, codeIntegrity);
        var parts = new Dictionary<string, byte[]>
        {
            ["[Content_Types].xml"] = contentTypes,
            ["AppxBlockMap.xml"] = blockMap,
            ["AppxMetadata/CodeIntegrity.cat"] = codeIntegrity,
        };

        using OpcPackage opc = BuildOpc(parts);
        AppxDigestTable table = ParseTableDirect(entries);

        IndirectDataBindingResult result = AppxDigestTableVerifier.Verify(table, opc);

        Assert.True(result.IsBindingValid);
        AssertTagStatus(result, AppxDigestTag.Axci, DigestVerificationStatus.Valid);
    }

    [Fact]
    public void Verify_AxciPresent_ButTampered_Fails()
    {
        byte[] contentTypes = "<Types />"u8.ToArray();
        byte[] blockMap = "<BlockMap />"u8.ToArray();
        byte[] codeIntegrity = new byte[] { 0xCA, 0xFE };

        var entries = MakeCorrectEntries(contentTypes, blockMap, codeIntegrity);
        // Tamper: put wrong CodeIntegrity.cat in the package.
        var parts = new Dictionary<string, byte[]>
        {
            ["[Content_Types].xml"] = contentTypes,
            ["AppxBlockMap.xml"] = blockMap,
            ["AppxMetadata/CodeIntegrity.cat"] = new byte[] { 0xDE, 0xAD },
        };

        using OpcPackage opc = BuildOpc(parts);
        AppxDigestTable table = ParseTableDirect(entries);

        IndirectDataBindingResult result = AppxDigestTableVerifier.Verify(table, opc);

        Assert.False(result.IsBindingValid);
        AssertTagStatus(result, AppxDigestTag.Axci, DigestVerificationStatus.Mismatch);
    }

    [Fact]
    public void Verify_AxciDigestPresent_ButPartMissing_Fails()
    {
        byte[] contentTypes = "<Types />"u8.ToArray();
        byte[] blockMap = "<BlockMap />"u8.ToArray();
        byte[] codeIntegrity = new byte[] { 0xCA, 0xFE };

        var entries = MakeCorrectEntries(contentTypes, blockMap, codeIntegrity);
        // Part is missing from the package.
        var parts = new Dictionary<string, byte[]>
        {
            ["[Content_Types].xml"] = contentTypes,
            ["AppxBlockMap.xml"] = blockMap,
        };

        using OpcPackage opc = BuildOpc(parts);
        AppxDigestTable table = ParseTableDirect(entries);

        IndirectDataBindingResult result = AppxDigestTableVerifier.Verify(table, opc);

        Assert.False(result.IsBindingValid);
        AssertTagStatus(result, AppxDigestTag.Axci, DigestVerificationStatus.PartMissing);
    }

    [Fact]
    public void Verify_AxciAbsent_NoPart_IsValid()
    {
        // No AXCI tag and no CodeIntegrity.cat part — binding should pass.
        byte[] contentTypes = "<Types />"u8.ToArray();
        byte[] blockMap = "<BlockMap />"u8.ToArray();

        var entries = MakeCorrectEntries(contentTypes, blockMap); // No AXCI.
        var parts = new Dictionary<string, byte[]>
        {
            ["[Content_Types].xml"] = contentTypes,
            ["AppxBlockMap.xml"] = blockMap,
        };

        using OpcPackage opc = BuildOpc(parts);
        AppxDigestTable table = ParseTableDirect(entries);

        IndirectDataBindingResult result = AppxDigestTableVerifier.Verify(table, opc);

        Assert.True(result.IsBindingValid);
    }

    [Fact]
    public void Verify_AxciAbsent_ButPartPresent_FailsBinding()
    {
        // AXCI tag absent but CodeIntegrity.cat exists in the package — an attacker added
        // an unsigned catalog. Binding MUST FAIL.
        byte[] contentTypes = "<Types />"u8.ToArray();
        byte[] blockMap = "<BlockMap />"u8.ToArray();

        var entries = MakeCorrectEntries(contentTypes, blockMap); // No AXCI tag.
        var parts = new Dictionary<string, byte[]>
        {
            ["[Content_Types].xml"] = contentTypes,
            ["AppxBlockMap.xml"] = blockMap,
            ["AppxMetadata/CodeIntegrity.cat"] = new byte[] { 0xDE, 0xAD }, // attacker-added
        };

        using OpcPackage opc = BuildOpc(parts);
        AppxDigestTable table = ParseTableDirect(entries);

        IndirectDataBindingResult result = AppxDigestTableVerifier.Verify(table, opc);

        Assert.False(result.IsBindingValid, "Part present without AXCI tag must fail binding.");
        AssertTagStatus(result, AppxDigestTag.Axci, DigestVerificationStatus.DigestMissing);
    }

    [Fact]
    public void Verify_AxciTagPresent_ButPartMissing_FailsBinding()
    {
        // AXCI tag present but CodeIntegrity.cat missing from package — confirms
        // both directions of the tag/part mismatch are caught.
        byte[] contentTypes = "<Types />"u8.ToArray();
        byte[] blockMap = "<BlockMap />"u8.ToArray();
        byte[] codeIntegrity = new byte[] { 0xCA, 0xFE };

        var entries = MakeCorrectEntries(contentTypes, blockMap, codeIntegrity);
        // Deliberately omit CodeIntegrity.cat from the package.
        var parts = new Dictionary<string, byte[]>
        {
            ["[Content_Types].xml"] = contentTypes,
            ["AppxBlockMap.xml"] = blockMap,
        };

        using OpcPackage opc = BuildOpc(parts);
        AppxDigestTable table = ParseTableDirect(entries);

        IndirectDataBindingResult result = AppxDigestTableVerifier.Verify(table, opc);

        Assert.False(result.IsBindingValid, "AXCI tag present with missing part must fail binding.");
        AssertTagStatus(result, AppxDigestTag.Axci, DigestVerificationStatus.PartMissing);
    }

    [Fact]
    public void Verify_AxpcAndAxcd_AreSurfacedAsNotVerified()
    {
        byte[] contentTypes = "<Types />"u8.ToArray();
        byte[] blockMap = "<BlockMap />"u8.ToArray();

        var entries = MakeCorrectEntries(contentTypes, blockMap);
        var parts = new Dictionary<string, byte[]>
        {
            ["[Content_Types].xml"] = contentTypes,
            ["AppxBlockMap.xml"] = blockMap,
        };

        using OpcPackage opc = BuildOpc(parts);
        AppxDigestTable table = ParseTableDirect(entries);

        IndirectDataBindingResult result = AppxDigestTableVerifier.Verify(table, opc);

        // AXPC and AXCD must be visible in results, not silently omitted.
        DigestEntryResult axpc = result.Results.Single(r => r.Tag == AppxDigestTag.Axpc);
        DigestEntryResult axcd = result.Results.Single(r => r.Tag == AppxDigestTag.Axcd);
        Assert.Equal(DigestVerificationStatus.NotVerified, axpc.Status);
        Assert.Equal(DigestVerificationStatus.NotVerified, axcd.Status);
        Assert.NotNull(axpc.Detail);
        Assert.NotNull(axcd.Detail);
        // Overall binding is still valid — NotVerified doesn't fail binding.
        Assert.True(result.IsBindingValid);
    }

    #endregion

    #region Stapling attack test — THE KEY SECURITY TEST

    /// <summary>
    /// Reproduces the signature-stapling attack:
    /// 1. Package A has content_A and a matching block map. A's signature digest table
    ///    binds to A's exact AppxBlockMap.xml and [Content_Types].xml.
    /// 2. Package B has different payload content_B and its own block map.
    /// 3. Attacker staples A's signature (with A's digest table) onto B.
    /// 4. The old code (CMS-only check) would pass. The new binding check MUST FAIL
    ///    because B's AppxBlockMap.xml differs from A's.
    /// </summary>
    [Fact]
    public void StaplingAttack_DetectedByBindingVerification()
    {
        // --- Package A: the legitimate package whose signature will be stolen ---
        byte[] contentTypesA = "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='xml' ContentType='application/xml'/></Types>"u8.ToArray();
        byte[] payloadA = "Hello from legitimate app A"u8.ToArray();
        string blockMapXmlA = PackageBuilder.BlockMapXml(
            new Dictionary<string, byte[]> { ["Assets/payload.txt"] = payloadA });
        byte[] blockMapBytesA = System.Text.Encoding.UTF8.GetBytes(blockMapXmlA);

        // Build digest entries that bind to package A's actual content.
        var entriesA = MakeCorrectEntries(contentTypesA, blockMapBytesA);

        // Sign with cert as if this were package A's signature.
        using X509Certificate2 cert = CreateCert("CN=Microsoft Corporation, O=Microsoft");
        byte[] p7xA = BuildP7x(cert, entriesA);

        // --- Package B: the attacker's malicious package ---
        byte[] payloadB = "EVIL PAYLOAD - not what was signed"u8.ToArray();
        string blockMapXmlB = PackageBuilder.BlockMapXml(
            new Dictionary<string, byte[]> { ["Assets/payload.txt"] = payloadB });
        byte[] blockMapBytesB = System.Text.Encoding.UTF8.GetBytes(blockMapXmlB);

        // The attacker uses the SAME [Content_Types].xml (might or might not match — here
        // the block map definitely differs, which is the critical binding).
        Assert.NotEqual(blockMapBytesA, blockMapBytesB); // Confirm they differ.

        // Build OPC for package B with A's stolen signature stapled in.
        var partsB = new Dictionary<string, byte[]>
        {
            ["[Content_Types].xml"] = contentTypesA,
            ["AppxBlockMap.xml"] = blockMapBytesB,    // B's block map — different from A's
            ["Assets/payload.txt"] = payloadB,
            ["AppxSignature.p7x"] = p7xA,             // A's stolen signature
        };

        using OpcPackage opcB = BuildOpc(partsB);

        // Read the stolen signature — CMS envelope check passes (it's genuinely signed).
        PackageSignature sig = PackageSignatureReader.Read(p7xA);
        Assert.True(sig.IsCmsIntegrityValid, "CMS envelope should be valid (it was genuinely signed).");
        Assert.NotNull(sig.DigestTable);

        // THE CRITICAL CHECK: binding verification must FAIL because B's AppxBlockMap.xml
        // does not match the AXBM digest that was computed over A's AppxBlockMap.xml.
        IndirectDataBindingResult binding = AppxDigestTableVerifier.Verify(sig.DigestTable!, opcB);

        Assert.False(binding.IsBindingValid, "Binding must FAIL — this is a stapled signature.");
        AssertTagStatus(binding, AppxDigestTag.Axbm, DigestVerificationStatus.Mismatch);

        // The structured result clearly tells the caller what went wrong.
        Assert.Contains("FAILED", binding.Summary);
    }

    #endregion

    #region CMS integrity gate

    [Fact]
    public void Read_CmsInvalid_DoesNotParseDigestTable()
    {
        // When CMS integrity fails, the digest table must NOT be parsed — its content is
        // attacker-controlled if the envelope is not valid.
        using X509Certificate2 cert = CreateCert();
        var entries = new[]
        {
            (AppxDigestTag.Axpc, RandomDigest()),
            (AppxDigestTag.Axcd, RandomDigest()),
            (AppxDigestTag.Axct, RandomDigest()),
            (AppxDigestTag.Axbm, RandomDigest()),
        };
        byte[] p7x = BuildP7x(cert, entries);

        // Corrupt a byte in the CMS (after the PKCX header) to break the signature.
        p7x[p7x.Length / 2] ^= 0xFF;

        // This may throw or return invalid — either is acceptable.
        try
        {
            PackageSignature sig = PackageSignatureReader.Read(p7x);
            // If it doesn't throw, the digest table must be null (CMS not valid).
            if (!sig.IsCmsIntegrityValid)
            {
                Assert.Null(sig.DigestTable);
            }
        }
        catch (InvalidDataException)
        {
            // Corrupted CMS may not even decode — that's fine.
        }
    }

    #endregion

    #region Integration through MsixPackage.VerifySignatureBinding

    [Fact]
    public void VerifySignatureBinding_ThrowsWhenCmsInvalid()
    {
        // Create a signature with invalid CMS.
        var sig = new PackageSignature
        {
            SubjectName = "CN=Test",
            SubjectNameRawData = ReadOnlyMemory<byte>.Empty,
            IssuerName = "CN=Test",
            Thumbprint = "AAAA",
            NotBefore = DateTimeOffset.UtcNow,
            NotAfter = DateTimeOffset.UtcNow,
            IsCmsIntegrityValid = false,
            DigestTable = null,
        };

        byte[] contentTypes = "<Types />"u8.ToArray();
        byte[] blockMap = "<BlockMap />"u8.ToArray();
        byte[] manifest = "<Package><Identity Name='Test' Version='1.0.0.0' Publisher='CN=Test' ProcessorArchitecture='x64'/></Package>"u8.ToArray();
        var parts = new Dictionary<string, byte[]>
        {
            ["[Content_Types].xml"] = contentTypes,
            ["AppxBlockMap.xml"] = blockMap,
            ["AppxManifest.xml"] = manifest,
        };

        using MsixPackage package = MsixPackage.Open(BuildOpcStream(parts));

        Assert.Throws<InvalidOperationException>(() => package.VerifySignatureBinding(sig));
    }

    [Fact]
    public void VerifySignatureBinding_ThrowsWhenDigestTableNull()
    {
        var sig = new PackageSignature
        {
            SubjectName = "CN=Test",
            SubjectNameRawData = ReadOnlyMemory<byte>.Empty,
            IssuerName = "CN=Test",
            Thumbprint = "AAAA",
            NotBefore = DateTimeOffset.UtcNow,
            NotAfter = DateTimeOffset.UtcNow,
            IsCmsIntegrityValid = true,
            DigestTable = null,
            DigestTableError = "some error",
        };

        byte[] contentTypes = "<Types />"u8.ToArray();
        byte[] blockMap = "<BlockMap />"u8.ToArray();
        byte[] manifest = "<Package><Identity Name='Test' Version='1.0.0.0' Publisher='CN=Test' ProcessorArchitecture='x64'/></Package>"u8.ToArray();
        var parts = new Dictionary<string, byte[]>
        {
            ["[Content_Types].xml"] = contentTypes,
            ["AppxBlockMap.xml"] = blockMap,
            ["AppxManifest.xml"] = manifest,
        };

        using MsixPackage package = MsixPackage.Open(BuildOpcStream(parts));

        Assert.Throws<InvalidOperationException>(() => package.VerifySignatureBinding(sig));
    }

    private static MemoryStream BuildOpcStream(Dictionary<string, byte[]> parts)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in parts)
            {
                using Stream entry = archive.CreateEntry(name).Open();
                entry.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    #endregion

    #region TOCTOU — single-read-single-hash across pipeline phases

    /// <summary>
    /// The definitive TOCTOU test: on a real <see cref="DirectoryOpcPackage"/>, swaps
    /// <c>AppxBlockMap.xml</c> on disk between payload verification and binding verification.
    ///
    /// Attack scenario:
    /// 1. Malicious block map B (matching malicious payload) is on disk during parsing + payload verify.
    /// 2. Attacker swaps to legitimate signed block map A before binding verification.
    /// 3. Without the fix: payload is verified against B, binding hashes A → both pass over different bytes.
    /// 4. With the fix: binding hashes the bytes cached from step 1 (B), which don't match the AXBM
    ///    digest (computed over A) → binding FAILS.
    /// </summary>
    [Fact]
    public void Toctou_SwapBlockMapAfterPayloadVerify_BindingFails()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"msixcore-toctou-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "Assets"));

            byte[] manifest = "<Package><Identity Name='Test' Version='1.0.0.0' Publisher='CN=Test' ProcessorArchitecture='x64'/></Package>"u8.ToArray();
            byte[] contentTypes = "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='txt' ContentType='text/plain'/></Types>"u8.ToArray();

            // --- Payload sets ---
            byte[] payloadA = "Hello from legitimate app A"u8.ToArray();
            byte[] payloadB = "EVIL PAYLOAD"u8.ToArray();

            // Build block maps covering both AppxManifest.xml and the payload file.
            var filesA = new Dictionary<string, byte[]>
            {
                ["AppxManifest.xml"] = manifest,
                ["Assets/payload.txt"] = payloadA,
            };
            var filesB = new Dictionary<string, byte[]>
            {
                ["AppxManifest.xml"] = manifest,
                ["Assets/payload.txt"] = payloadB,
            };

            byte[] blockMapBytesA = System.Text.Encoding.UTF8.GetBytes(PackageBuilder.BlockMapXml(filesA));
            byte[] blockMapBytesB = System.Text.Encoding.UTF8.GetBytes(PackageBuilder.BlockMapXml(filesB));
            Assert.NotEqual(blockMapBytesA, blockMapBytesB);

            // Build digest table over A's block map (as if signed over A).
            var entries = MakeCorrectEntries(contentTypes, blockMapBytesA);

            // Write malicious layout B to disk.
            File.WriteAllBytes(Path.Combine(dir, "[Content_Types].xml"), contentTypes);
            File.WriteAllBytes(Path.Combine(dir, "AppxBlockMap.xml"), blockMapBytesB);
            File.WriteAllBytes(Path.Combine(dir, "AppxManifest.xml"), manifest);
            File.WriteAllBytes(Path.Combine(dir, "Assets", "payload.txt"), payloadB);

            // Open as a directory package — this reads and caches block map B's bytes.
            using MsixPackage package = MsixPackage.OpenDirectory(dir);

            // Phase 1: payload verification against block map B — passes (payload matches B).
            BlockMapVerificationResult bmResult = package.VerifyBlockMap();
            Assert.True(bmResult.IsValid, "Payload should match block map B.");

            // --- ATTACKER SWAPS THE FILE ON DISK ---
            File.WriteAllBytes(Path.Combine(dir, "AppxBlockMap.xml"), blockMapBytesA);

            // Build a fake signature whose digest table binds to A's block map.
            AppxDigestTable table = ParseTableDirect(entries);
            var fakeSig = new PackageSignature
            {
                SubjectName = "CN=Test",
                SubjectNameRawData = ReadOnlyMemory<byte>.Empty,
                IssuerName = "CN=Test",
                Thumbprint = "AAAA",
                NotBefore = DateTimeOffset.UtcNow,
                NotAfter = DateTimeOffset.UtcNow,
                IsCmsIntegrityValid = true,
                DigestTable = table,
            };

            // Phase 2: binding verification. Without fix, re-reads from disk (now A) → passes.
            // With fix, hashes cached bytes from phase 1 (B) → AXBM mismatch.
            IndirectDataBindingResult binding = package.VerifySignatureBinding(fakeSig);

            Assert.False(binding.IsBindingValid,
                "Binding must FAIL: cached block map bytes (B) don't match signed AXBM digest (over A).");
            AssertTagStatus(binding, AppxDigestTag.Axbm, DigestVerificationStatus.Mismatch);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Inverse ordering: the block map parser reads A (legitimate) at package open time,
    /// then the attacker swaps to B (malicious payload matching B's block map) before
    /// <see cref="MsixPackage.VerifyBlockMap"/>. Payload verification should fail because
    /// the cached parsed block map (A) doesn't match the swapped payload (B).
    /// </summary>
    [Fact]
    public void Toctou_SwapBlockMapBeforePayloadVerify_PayloadFails()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"msixcore-toctou2-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "Assets"));

            byte[] manifest = "<Package><Identity Name='Test' Version='1.0.0.0' Publisher='CN=Test' ProcessorArchitecture='x64'/></Package>"u8.ToArray();
            byte[] contentTypes = "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='txt' ContentType='text/plain'/></Types>"u8.ToArray();

            byte[] payloadA = "Hello from legitimate app A"u8.ToArray();
            byte[] payloadB = "EVIL PAYLOAD"u8.ToArray();

            var filesA = new Dictionary<string, byte[]>
            {
                ["AppxManifest.xml"] = manifest,
                ["Assets/payload.txt"] = payloadA,
            };
            byte[] blockMapBytesA = System.Text.Encoding.UTF8.GetBytes(PackageBuilder.BlockMapXml(filesA));

            var filesB = new Dictionary<string, byte[]>
            {
                ["AppxManifest.xml"] = manifest,
                ["Assets/payload.txt"] = payloadB,
            };
            byte[] blockMapBytesB = System.Text.Encoding.UTF8.GetBytes(PackageBuilder.BlockMapXml(filesB));

            // Write legitimate layout A to disk.
            File.WriteAllBytes(Path.Combine(dir, "[Content_Types].xml"), contentTypes);
            File.WriteAllBytes(Path.Combine(dir, "AppxBlockMap.xml"), blockMapBytesA);
            File.WriteAllBytes(Path.Combine(dir, "AppxManifest.xml"), manifest);
            File.WriteAllBytes(Path.Combine(dir, "Assets", "payload.txt"), payloadA);

            // Open — this reads and caches block map A.
            using MsixPackage package = MsixPackage.OpenDirectory(dir);

            // Force block map parsing now (caches A's bytes).
            _ = package.BlockMap;

            // --- ATTACKER SWAPS ---
            // Replace block map with B and payload with B's matching payload.
            File.WriteAllBytes(Path.Combine(dir, "AppxBlockMap.xml"), blockMapBytesB);
            File.WriteAllBytes(Path.Combine(dir, "Assets", "payload.txt"), payloadB);

            // Payload verification uses the cached parsed block map (A) against live payload (B).
            // The hashes won't match → payload verification fails.
            BlockMapVerificationResult bmResult = package.VerifyBlockMap();
            Assert.False(bmResult.IsValid,
                "Payload verification must FAIL: cached block map A vs swapped payload B.");
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Reverse-order TOCTOU: <see cref="MsixPackage.VerifySignatureBinding"/> runs first
    /// (caching block map A from disk), then the attacker swaps to block map B before
    /// <see cref="MsixPackage.VerifyBlockMap"/>. Without the single choke-point,
    /// <c>ReadBlockMap</c> would re-read B from disk (overwriting the cache), and payload
    /// verification would pass against B — while binding already verified A.
    ///
    /// With the fix, <c>ReadBlockMap</c> goes through <c>GetFootprintBytes</c> which returns
    /// the already-cached A, so payload verification runs against A and fails (payload is B).
    /// </summary>
    [Fact]
    public void Toctou_BindingFirst_ThenSwapBeforePayloadVerify_PayloadFails()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"msixcore-toctou3-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "Assets"));

            byte[] manifest = "<Package><Identity Name='Test' Version='1.0.0.0' Publisher='CN=Test' ProcessorArchitecture='x64'/></Package>"u8.ToArray();
            byte[] contentTypes = "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='txt' ContentType='text/plain'/></Types>"u8.ToArray();

            byte[] payloadA = "Hello from legitimate app A"u8.ToArray();
            byte[] payloadB = "EVIL PAYLOAD"u8.ToArray();

            var filesA = new Dictionary<string, byte[]>
            {
                ["AppxManifest.xml"] = manifest,
                ["Assets/payload.txt"] = payloadA,
            };
            var filesB = new Dictionary<string, byte[]>
            {
                ["AppxManifest.xml"] = manifest,
                ["Assets/payload.txt"] = payloadB,
            };

            byte[] blockMapBytesA = System.Text.Encoding.UTF8.GetBytes(PackageBuilder.BlockMapXml(filesA));
            byte[] blockMapBytesB = System.Text.Encoding.UTF8.GetBytes(PackageBuilder.BlockMapXml(filesB));

            // Write legitimate layout A to disk (block map A, payload A).
            File.WriteAllBytes(Path.Combine(dir, "[Content_Types].xml"), contentTypes);
            File.WriteAllBytes(Path.Combine(dir, "AppxBlockMap.xml"), blockMapBytesA);
            File.WriteAllBytes(Path.Combine(dir, "AppxManifest.xml"), manifest);
            File.WriteAllBytes(Path.Combine(dir, "Assets", "payload.txt"), payloadA);

            using MsixPackage package = MsixPackage.OpenDirectory(dir);

            // Phase 1: binding first (before .BlockMap is ever accessed).
            // This caches block map A's bytes via GetFootprintBytes.
            var entries = MakeCorrectEntries(contentTypes, blockMapBytesA);
            AppxDigestTable table = ParseTableDirect(entries);
            var fakeSig = new PackageSignature
            {
                SubjectName = "CN=Test",
                SubjectNameRawData = ReadOnlyMemory<byte>.Empty,
                IssuerName = "CN=Test",
                Thumbprint = "AAAA",
                NotBefore = DateTimeOffset.UtcNow,
                NotAfter = DateTimeOffset.UtcNow,
                IsCmsIntegrityValid = true,
                DigestTable = table,
            };

            IndirectDataBindingResult binding = package.VerifySignatureBinding(fakeSig);
            Assert.True(binding.IsBindingValid, "Binding should pass — block map A matches AXBM digest over A.");

            // --- ATTACKER SWAPS ---
            // Replace block map with B and payload with B's payload on disk.
            File.WriteAllBytes(Path.Combine(dir, "AppxBlockMap.xml"), blockMapBytesB);
            File.WriteAllBytes(Path.Combine(dir, "Assets", "payload.txt"), payloadB);

            // Phase 2: payload verification. ReadBlockMap goes through GetFootprintBytes,
            // which returns cached A (not the swapped B on disk). The parsed block map is A,
            // but the payload on disk is B → hash mismatch → verification fails.
            BlockMapVerificationResult bmResult = package.VerifyBlockMap();
            Assert.False(bmResult.IsValid,
                "Payload verification must FAIL: ReadBlockMap used cached A, but payload on disk is B.");
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    #endregion

    #region Alert 1 — Verify(table, opc) is internal, not a public bypass

    [Fact]
    public void VerifyOpcOverload_IsInternal_NotPublicApi()
    {
        // The Verify(AppxDigestTable, IOpcPackage) overload must not be public — it bypasses
        // the MsixPackage footprint cache and TOCTOU protection. Confirm via reflection that
        // it is not publicly accessible.
        var method = typeof(AppxDigestTableVerifier).GetMethod(
            "Verify",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            [typeof(AppxDigestTable), typeof(IOpcPackage)]);

        Assert.Null(method); // Must not be found as a public method.
    }

    #endregion

    #region Alert 2 — Concurrent access returns consistent bytes

    [Fact]
    public void Concurrent_VerifyBlockMapAndBinding_SameBytes()
    {
        // This test exercises the lock by running VerifyBlockMap and VerifySignatureBinding
        // concurrently from multiple threads. Both must observe the same block map bytes.
        // On a mutable directory without the lock, the two threads could both miss the cache
        // and read at different times.
        string dir = Path.Combine(Path.GetTempPath(), $"msixcore-conc-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "Assets"));

            byte[] manifest = "<Package><Identity Name='Test' Version='1.0.0.0' Publisher='CN=Test' ProcessorArchitecture='x64'/></Package>"u8.ToArray();
            byte[] contentTypes = "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='txt' ContentType='text/plain'/></Types>"u8.ToArray();
            byte[] payload = "test payload"u8.ToArray();
            var files = new Dictionary<string, byte[]>
            {
                ["AppxManifest.xml"] = manifest,
                ["Assets/payload.txt"] = payload,
            };
            byte[] blockMapBytes = System.Text.Encoding.UTF8.GetBytes(PackageBuilder.BlockMapXml(files));

            File.WriteAllBytes(Path.Combine(dir, "[Content_Types].xml"), contentTypes);
            File.WriteAllBytes(Path.Combine(dir, "AppxBlockMap.xml"), blockMapBytes);
            File.WriteAllBytes(Path.Combine(dir, "AppxManifest.xml"), manifest);
            File.WriteAllBytes(Path.Combine(dir, "Assets", "payload.txt"), payload);

            using MsixPackage package = MsixPackage.OpenDirectory(dir);

            var entries = MakeCorrectEntries(contentTypes, blockMapBytes);
            AppxDigestTable table = ParseTableDirect(entries);
            var fakeSig = new PackageSignature
            {
                SubjectName = "CN=Test",
                SubjectNameRawData = ReadOnlyMemory<byte>.Empty,
                IssuerName = "CN=Test",
                Thumbprint = "AAAA",
                NotBefore = DateTimeOffset.UtcNow,
                NotAfter = DateTimeOffset.UtcNow,
                IsCmsIntegrityValid = true,
                DigestTable = table,
            };

            // Run both operations concurrently multiple times.
            bool allBindingValid = true;
            bool allBlockMapValid = true;
            Parallel.For(0, 20, _ =>
            {
                IndirectDataBindingResult binding = package.VerifySignatureBinding(fakeSig);
                if (!binding.IsBindingValid) allBindingValid = false;

                BlockMapVerificationResult bm = package.VerifyBlockMap();
                if (!bm.IsValid) allBlockMapValid = false;
            });

            Assert.True(allBindingValid, "All concurrent binding verifications must pass.");
            Assert.True(allBlockMapValid, "All concurrent block map verifications must pass.");
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    #endregion

    #region Alert 3 — Post-open footprint drift detected

    [Fact]
    public void DriftDetection_CodeIntegrityCatCreatedAfterOpen_BindingFails()
    {
        // Open a directory package that has NO CodeIntegrity.cat.
        // After open, attacker creates CodeIntegrity.cat on disk.
        // VerifySignatureBinding must fail closed — the drift detection must catch it.
        string dir = Path.Combine(Path.GetTempPath(), $"msixcore-drift-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "Assets"));

            byte[] manifest = "<Package><Identity Name='Test' Version='1.0.0.0' Publisher='CN=Test' ProcessorArchitecture='x64'/></Package>"u8.ToArray();
            byte[] contentTypes = "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='txt' ContentType='text/plain'/></Types>"u8.ToArray();
            byte[] payload = "legitimate payload"u8.ToArray();
            var files = new Dictionary<string, byte[]>
            {
                ["AppxManifest.xml"] = manifest,
                ["Assets/payload.txt"] = payload,
            };
            byte[] blockMapBytes = System.Text.Encoding.UTF8.GetBytes(PackageBuilder.BlockMapXml(files));

            File.WriteAllBytes(Path.Combine(dir, "[Content_Types].xml"), contentTypes);
            File.WriteAllBytes(Path.Combine(dir, "AppxBlockMap.xml"), blockMapBytes);
            File.WriteAllBytes(Path.Combine(dir, "AppxManifest.xml"), manifest);
            File.WriteAllBytes(Path.Combine(dir, "Assets", "payload.txt"), payload);

            // Open — no CodeIntegrity.cat at this point.
            using MsixPackage package = MsixPackage.OpenDirectory(dir);

            // Attacker creates CodeIntegrity.cat after open.
            Directory.CreateDirectory(Path.Combine(dir, "AppxMetadata"));
            File.WriteAllBytes(Path.Combine(dir, "AppxMetadata", "CodeIntegrity.cat"),
                new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

            // Build a valid digest table (no AXCI — the original package didn't have it).
            var entries = MakeCorrectEntries(contentTypes, blockMapBytes);
            AppxDigestTable table = ParseTableDirect(entries);
            var fakeSig = new PackageSignature
            {
                SubjectName = "CN=Test",
                SubjectNameRawData = ReadOnlyMemory<byte>.Empty,
                IssuerName = "CN=Test",
                Thumbprint = "AAAA",
                NotBefore = DateTimeOffset.UtcNow,
                NotAfter = DateTimeOffset.UtcNow,
                IsCmsIntegrityValid = true,
                DigestTable = table,
            };

            // Must fail closed — attacker-created CodeIntegrity.cat detected.
            IndirectDataBindingResult binding = package.VerifySignatureBinding(fakeSig);

            Assert.False(binding.IsBindingValid,
                "Binding must FAIL: CodeIntegrity.cat appeared after open — drift detected.");
            Assert.Contains("now exists on disk", binding.Summary);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void DriftDetection_ContainerPackage_NoDriftCheckNeeded()
    {
        // Container (ZIP) packages cannot have post-open drift — confirm no false positive.
        byte[] contentTypes = "<Types />"u8.ToArray();
        byte[] blockMap = "<BlockMap />"u8.ToArray();
        byte[] manifest = "<Package><Identity Name='Test' Version='1.0.0.0' Publisher='CN=Test' ProcessorArchitecture='x64'/></Package>"u8.ToArray();

        var entries = MakeCorrectEntries(contentTypes, blockMap);
        var parts = new Dictionary<string, byte[]>
        {
            ["[Content_Types].xml"] = contentTypes,
            ["AppxBlockMap.xml"] = blockMap,
            ["AppxManifest.xml"] = manifest,
        };

        using MsixPackage package = MsixPackage.Open(BuildOpcStream(parts));

        AppxDigestTable table = ParseTableDirect(entries);
        var fakeSig = new PackageSignature
        {
            SubjectName = "CN=Test",
            SubjectNameRawData = ReadOnlyMemory<byte>.Empty,
            IssuerName = "CN=Test",
            Thumbprint = "AAAA",
            NotBefore = DateTimeOffset.UtcNow,
            NotAfter = DateTimeOffset.UtcNow,
            IsCmsIntegrityValid = true,
            DigestTable = table,
        };

        IndirectDataBindingResult binding = package.VerifySignatureBinding(fakeSig);
        Assert.True(binding.IsBindingValid);
    }

    #endregion

    #region Helper methods

    /// <summary>Directly constructs an <see cref="AppxDigestTable"/> for verifier tests (bypasses CMS).</summary>
    private static AppxDigestTable ParseTableDirect((AppxDigestTag tag, byte[] digest)[] entries)
    {
        byte[] tableBytes = BuildDigestTableBytes(entries);
        byte[] spcContent = BuildSpcIndirectDataContent(tableBytes);

        using X509Certificate2 cert = CreateCert();
        var contentInfo = new ContentInfo(new Oid(SpcIndirectDataOid), spcContent);
        var cms = new SignedCms(contentInfo, detached: false);
        var signer = new CmsSigner(cert) { IncludeOption = X509IncludeOption.EndCertOnly };
        cms.ComputeSignature(signer);

        return AppxDigestTable.Parse(cms);
    }

    private static void AssertTagStatus(IndirectDataBindingResult result, AppxDigestTag tag, DigestVerificationStatus expected)
    {
        DigestEntryResult? entry = result.Results.FirstOrDefault(r => r.Tag == tag);
        Assert.NotNull(entry);
        Assert.Equal(expected, entry!.Status);
    }

    #endregion
}
