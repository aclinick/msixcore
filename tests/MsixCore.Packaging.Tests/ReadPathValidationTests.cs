using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using MsixCore.Packaging.Authoring;
using MsixCore.Packaging.Integrity;
using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging.Tests;

public sealed class ReadPathValidationTests : IDisposable
{
    private const string Manifest =
        """
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
          <Identity Name="Contoso.ReadPath" Publisher="CN=Contoso" Version="1.0.0.0" ProcessorArchitecture="x64" />
          <Properties><DisplayName>Read path</DisplayName><PublisherDisplayName>Contoso</PublisherDisplayName></Properties>
        </Package>
        """;

    private static readonly XNamespace ContentTypesNamespace =
        "http://schemas.openxmlformats.org/package/2006/content-types";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "msix-read-path-" + Guid.NewGuid().ToString("N"));

    public ReadPathValidationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TC_P0_3a_CompressedSizeMismatch_IsRejected()
    {
        string path = Build("compressed-mismatch", CompressionLevel.Optimal);
        using MsixPackage package = MsixPackage.Open(path);
        BlockMapFile original = PayloadFile(package);
        BlockMapFile corrupted = ReplaceFirstCompressedSize(original, original.Blocks[0].CompressedSize!.Value + 1);

        BlockMapVerificationResult result = BlockMapVerifier.Verify(
            package.Opc,
            ReplaceFile(package.BlockMap, corrupted));

        Assert.Contains(
            result.CoverageErrors,
            static error => error.Contains("compressed-size mismatch", StringComparison.Ordinal));
    }

    [Fact]
    public void TC_P0_3b_StoredFileWithoutCompressedSizes_Passes()
    {
        string path = Build("stored", CompressionLevel.NoCompression);
        using MsixPackage package = MsixPackage.Open(path);
        BlockMapFile payload = PayloadFile(package);

        Assert.False(package.Opc.GetZipInfo(payload.Name)!.IsCompressed);
        Assert.All(payload.Blocks, static block => Assert.Null(block.CompressedSize));
        Assert.True(package.VerifyBlockMap().IsValid);
    }

    [Fact]
    public void TC_P0_3c_ZeroByteFileWithoutBlocks_Passes()
    {
        string path = Build("empty", CompressionLevel.NoCompression, emptyPayload: true);
        using MsixPackage package = MsixPackage.Open(path);
        BlockMapFile payload = PayloadFile(package);

        Assert.Equal(0, payload.Size);
        Assert.Empty(payload.Blocks);
        Assert.True(package.VerifyBlockMap().IsValid);
    }

    [Fact]
    public void CompressedSizeTwoByteFullFlushAllowance_Passes()
    {
        string path = Build("two-byte-allowance", CompressionLevel.Optimal);
        using MsixPackage package = MsixPackage.Open(path);
        BlockMapFile payload = PayloadFile(package);
        OpcPartZipInfo zip = package.Opc.GetZipInfo(payload.Name)!;

        Assert.Equal(2, zip.CompressedSize - payload.Blocks.Sum(static block => block.CompressedSize!.Value));
        Assert.True(package.VerifyBlockMap().IsValid);
    }

    [Fact]
    public void CompressedSizeThreeByteDiscrepancy_IsRejected()
    {
        string path = Build("three-byte-discrepancy", CompressionLevel.Optimal);
        using MsixPackage package = MsixPackage.Open(path);
        BlockMapFile original = PayloadFile(package);
        BlockMapFile corrupted = ReplaceFirstCompressedSize(original, original.Blocks[0].CompressedSize!.Value - 1);

        BlockMapVerificationResult result = BlockMapVerifier.Verify(
            package.Opc,
            ReplaceFile(package.BlockMap, corrupted));

        Assert.Contains(
            result.CoverageErrors,
            static error => error.Contains("compressed-size mismatch", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateBlockMapFileEntry_IsRejected()
    {
        string path = Build("duplicate", CompressionLevel.NoCompression);
        using MsixPackage package = MsixPackage.Open(path);
        BlockMapFile duplicate = PayloadFile(package);
        BlockMap map = package.BlockMap with { Files = [.. package.BlockMap.Files, duplicate] };

        BlockMapVerificationResult result = BlockMapVerifier.Verify(package.Opc, map);

        Assert.Contains(
            result.CoverageErrors,
            static error => error.Contains("duplicate file entry", StringComparison.Ordinal));
    }

    [Fact]
    public void NonEmptyFileWithZeroBlocks_IsRejected()
    {
        string path = Build("zero-blocks", CompressionLevel.NoCompression);
        using MsixPackage package = MsixPackage.Open(path);
        BlockMapFile original = PayloadFile(package);
        BlockMapFile corrupted = original with { Blocks = [] };

        BlockMapVerificationResult result = BlockMapVerifier.Verify(
            package.Opc,
            ReplaceFile(package.BlockMap, corrupted));

        Assert.Contains(
            result.CoverageErrors,
            static error => error.Contains("non-empty but declares zero blocks", StringComparison.Ordinal));
    }

    [Fact]
    public void TC_P0_4a_MissingContentTypes_ProducesDiagnostic()
    {
        string path = Build("missing-content-types", CompressionLevel.NoCompression);
        RewritePackage(path, zip => zip.GetEntry(OpcPartNames.ContentTypes)!.Delete());

        using MsixPackage package = MsixPackage.Open(path);
        BlockMapVerificationResult result = package.VerifyBlockMap();

        Assert.Contains(
            result.CoverageErrors,
            static error => error.Contains("missing the required '[Content_Types].xml'", StringComparison.Ordinal));
    }

    [Fact]
    public void TC_P0_4b_UncoveredPayloadExtension_ProducesDiagnostic()
    {
        string path = Build("uncovered-extension", CompressionLevel.NoCompression);
        RewriteContentTypes(path, document =>
            document.Root!.Elements(ContentTypesNamespace + "Default")
                .Single(element => element.Attribute("Extension")!.Value.Equals("bin", StringComparison.OrdinalIgnoreCase))
                .Remove());

        using MsixPackage package = MsixPackage.Open(path);
        BlockMapVerificationResult result = package.VerifyBlockMap();

        Assert.Contains(
            result.CoverageErrors,
            static error => error.Contains("Data/payload.bin", StringComparison.Ordinal)
                && error.Contains("no content type", StringComparison.Ordinal));
    }

    [Fact]
    public void TC_P0_4c_CodeIntegrityCatalogRemainsAnUnmappedFootprint()
    {
        string path = Build("catalog", CompressionLevel.NoCompression);
        RewritePackage(
            path,
            zip =>
            {
                WriteEntry(zip, OpcPartNames.CodeIntegrityCatalog, [1, 2, 3]);
                XDocument contentTypes = ReadXml(zip, OpcPartNames.ContentTypes);
                contentTypes.Root!.Add(
                    new XElement(
                        ContentTypesNamespace + "Default",
                        new XAttribute("Extension", "cat"),
                        new XAttribute("ContentType", "application/octet-stream")));
                WriteEntry(zip, OpcPartNames.ContentTypes, Serialize(contentTypes));
            });

        using MsixPackage package = MsixPackage.Open(path);
        BlockMapVerificationResult result = package.VerifyBlockMap();

        Assert.True(result.IsValid);
        Assert.DoesNotContain(
            package.BlockMap.Files,
            static file => file.Name.Equals(OpcPartNames.CodeIntegrityCatalog, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ContentTypesPartListedInBlockMap_IsRejected()
    {
        string path = Build("mapped-content-types", CompressionLevel.NoCompression);
        RewritePackage(
            path,
            zip =>
            {
                byte[] contentTypes = ReadBytes(zip, OpcPartNames.ContentTypes);
                XDocument blockMap = ReadXml(zip, OpcPartNames.AppxBlockMap);
                blockMap.Root!.Add(
                    new XElement(
                        blockMap.Root.Name.Namespace + "File",
                        new XAttribute("Name", OpcPartNames.ContentTypes),
                        new XAttribute("Size", contentTypes.Length),
                        new XAttribute("LfhSize", 0),
                        new XElement(
                            blockMap.Root.Name.Namespace + "Block",
                            new XAttribute("Hash", Convert.ToBase64String(SHA256.HashData(contentTypes))))));
                WriteEntry(zip, OpcPartNames.AppxBlockMap, Serialize(blockMap));
            });

        using MsixPackage package = MsixPackage.Open(path);
        BlockMapVerificationResult result = package.VerifyBlockMap();

        Assert.Contains(
            result.CoverageErrors,
            static error => error.Contains("must not appear in the block map", StringComparison.Ordinal));
    }

    [Fact]
    public void CanonicalOverrideWithoutExtensionDefault_CoversPayload()
    {
        string path = Build(
            "override",
            CompressionLevel.NoCompression,
            payloadName: "Data/space name.custom");
        RewriteContentTypes(
            path,
            document =>
            {
                document.Root!.Elements(ContentTypesNamespace + "Default")
                    .Single(element => element.Attribute("Extension")!.Value.Equals("custom", StringComparison.OrdinalIgnoreCase))
                    .Remove();
                document.Root.Add(
                    new XElement(
                        ContentTypesNamespace + "Override",
                        new XAttribute("PartName", "/Data/space%20name.custom"),
                        new XAttribute("ContentType", "application/octet-stream")));
            });

        using MsixPackage package = MsixPackage.Open(path);

        Assert.True(package.VerifyBlockMap().IsValid);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("notamediatype")]
    [InlineData("application/")]
    [InlineData("/octet-stream")]
    [InlineData("application/octet stream")]
    public void ContentTypeWithoutAWellFormedMediaType_IsRejected(string contentType)
    {
        string path = Build("bad-content-type", CompressionLevel.NoCompression);
        RewriteContentTypes(
            path,
            document => document.Root!.Elements(ContentTypesNamespace + "Default")
                .Single(element => element.Attribute("Extension")!.Value.Equals("bin", StringComparison.OrdinalIgnoreCase))
                .SetAttributeValue("ContentType", contentType));

        using MsixPackage package = MsixPackage.Open(path);

        BlockMapVerificationResult result = package.VerifyBlockMap();
        Assert.False(result.IsValid);
        Assert.Contains(result.CoverageErrors, error => error.Contains("invalid ContentType", StringComparison.Ordinal));
    }

    [Fact]
    public void ContentTypeWithMediaTypeParameters_IsAccepted()
    {
        string path = Build("parameterized-content-type", CompressionLevel.NoCompression);
        RewriteContentTypes(
            path,
            document => document.Root!.Elements(ContentTypesNamespace + "Default")
                .Single(element => element.Attribute("Extension")!.Value.Equals("bin", StringComparison.OrdinalIgnoreCase))
                .SetAttributeValue("ContentType", "application/octet-stream; charset=utf-8"));

        using MsixPackage package = MsixPackage.Open(path);

        Assert.True(package.VerifyBlockMap().IsValid);
    }

    [Fact]
    public void ExtensionContainingWhitespace_IsRejected()
    {
        string path = Build("bad-extension", CompressionLevel.NoCompression);
        RewriteContentTypes(
            path,
            document => document.Root!.Add(
                new XElement(
                    ContentTypesNamespace + "Default",
                    new XAttribute("Extension", "b in"),
                    new XAttribute("ContentType", "application/octet-stream"))));

        using MsixPackage package = MsixPackage.Open(path);

        BlockMapVerificationResult result = package.VerifyBlockMap();
        Assert.False(result.IsValid);
        Assert.Contains(result.CoverageErrors, error => error.Contains("invalid Extension", StringComparison.Ordinal));
    }

    [Fact]
    public void TrailingBytesAfterEndOfCentralDirectory_AreRejected()
    {
        // The runtime's ZIP reader tolerates trailing bytes after the end-of-central-directory record,
        // which would leave room for a second, decoy directory. Reject the archive outright instead.
        string path = Build("trailing-bytes", CompressionLevel.NoCompression);
        byte[] original = File.ReadAllBytes(path);
        File.WriteAllBytes(path, [.. original, .. new byte[16]]);

        Assert.Throws<InvalidDataException>(() => MsixPackage.Open(path));
    }

    [Fact]
    public void DecoyEndOfCentralDirectoryRecord_IsRejected()
    {
        string path = Build("decoy-eocd", CompressionLevel.NoCompression);
        byte[] original = File.ReadAllBytes(path);

        // Claim the real record has a 22-byte comment, then hide a decoy record inside that comment.
        int eocd = original.Length - 22;
        Assert.Equal(0x06054B50u, BitConverter.ToUInt32(original, eocd));
        BitConverter.GetBytes((ushort)22).CopyTo(original, eocd + 20);

        var decoy = new byte[22];
        BitConverter.GetBytes(0x06054B50u).CopyTo(decoy, 0);
        BitConverter.GetBytes((ushort)5000).CopyTo(decoy, 20);
        File.WriteAllBytes(path, [.. original, .. decoy]);

        Assert.Throws<InvalidDataException>(() => MsixPackage.Open(path));
    }

    private string Build(
        string name,
        CompressionLevel compressionLevel,
        bool emptyPayload = false,
        string payloadName = "Data/payload.bin")
    {
        string path = Path.Combine(_root, name + ".msix");
        using var manifest = new MemoryStream(Encoding.UTF8.GetBytes(Manifest));
        using var payload = new MemoryStream(
            emptyPayload ? [] : Enumerable.Repeat((byte)'A', BlockMap.BlockSize + 17).ToArray());
        new MsixPackageBuilder()
            .SetManifest(manifest)
            .AddFile(payloadName, payload)
            .Build(path, new PackOptions { CompressionLevel = compressionLevel });
        return path;
    }

    private static BlockMapFile PayloadFile(MsixPackage package) =>
        package.BlockMap.Files.Single(static file => file.Name.StartsWith("Data/", StringComparison.Ordinal));

    private static BlockMap ReplaceFile(BlockMap map, BlockMapFile replacement) =>
        map with
        {
            Files = map.Files
                .Select(file => file.Name == replacement.Name ? replacement : file)
                .ToArray(),
        };

    private static BlockMapFile ReplaceFirstCompressedSize(BlockMapFile file, long compressedSize) =>
        file with
        {
            Blocks =
            [
                file.Blocks[0] with { CompressedSize = compressedSize },
                .. file.Blocks.Skip(1),
            ],
        };

    private static void RewriteContentTypes(string path, Action<XDocument> mutate) =>
        RewritePackage(
            path,
            zip =>
            {
                XDocument contentTypes = ReadXml(zip, OpcPartNames.ContentTypes);
                mutate(contentTypes);
                WriteEntry(zip, OpcPartNames.ContentTypes, Serialize(contentTypes));
            });

    private static void RewritePackage(string path, Action<ZipArchive> mutate)
    {
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Update);
        mutate(zip);
    }

    private static XDocument ReadXml(ZipArchive zip, string name) =>
        XDocument.Load(new MemoryStream(ReadBytes(zip, name), writable: false));

    private static byte[] ReadBytes(ZipArchive zip, string name)
    {
        using Stream source = zip.GetEntry(name)!.Open();
        using var bytes = new MemoryStream();
        source.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static byte[] Serialize(XDocument document)
    {
        using var bytes = new MemoryStream();
        document.Save(bytes, SaveOptions.DisableFormatting);
        return bytes.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] content)
    {
        zip.GetEntry(name)?.Delete();
        using Stream destination = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
        destination.Write(content);
    }
}
