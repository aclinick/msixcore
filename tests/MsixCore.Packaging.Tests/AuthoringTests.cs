using System.Buffers.Binary;
using System.IO.Compression;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using MsixCore.Deployment;
using MsixCore.Packaging.Authoring;
using MsixCore.Packaging.Integrity;
using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging.Tests;

public sealed class AuthoringTests : IDisposable
{
    private const string Manifest =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
          <Identity Name="Contoso.Authored" Publisher="CN=Contoso" Version="2.3.4.5" ProcessorArchitecture="x64" />
          <Properties>
            <DisplayName>Authored package</DisplayName>
            <PublisherDisplayName>Contoso Ltd</PublisherDisplayName>
          </Properties>
        </Package>
        """;

    private readonly string _root;

    public AuthoringTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msix-authoring-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void BlockMapWriter_KnownInputs_ProducesExpectedSha256Blocks()
    {
        byte[] multiBlock =
        [
            .. Enumerable.Repeat(Enumerable.Range(0, 256).Select(static value => (byte)value), 256).SelectMany(static bytes => bytes),
            (byte)'Z',
        ];
        using var copied = new MemoryStream();

        BlockMapFile small = BlockMapWriter.CopyAndHash(
            "A&B.txt",
            new MemoryStream("abc"u8.ToArray()),
            Stream.Null);
        BlockMapFile large = BlockMapWriter.CopyAndHash(
            "nested/large.bin",
            new MemoryStream(multiBlock),
            copied);
        BlockMapFile empty = BlockMapWriter.CopyAndHash(
            "empty.dat",
            new MemoryStream(),
            Stream.Null);

        Assert.Equal("ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=", Assert.Single(small.Blocks).Hash);
        Assert.Equal(
            ["fayiCV0EOCYPqEkYPfxn+qRZ/fSTbhvJHuxrKBsn5MI=", "u+69h54d/2kYVG3AwXn93lBfKiFZHJqcluNrBU7Fr4M="],
            large.Blocks.Select(static block => block.Hash));
        Assert.Equal(multiBlock, copied.ToArray());
        Assert.Empty(empty.Blocks);
        Assert.Equal(0, empty.Size);

        XDocument document = LoadXml(BlockMapWriter.Write(
        [
            new AuthoredBlockMapFile(small, 37),
            new AuthoredBlockMapFile(large, 46),
            new AuthoredBlockMapFile(empty, 39),
        ]));
        Assert.Contains(document.Root!.Elements(), element => element.Attribute("Name")!.Value == "A&B.txt");
        XElement emptyElement = document.Root!.Elements().Single(element => element.Attribute("Name")!.Value == "empty.dat");
        Assert.Equal("39", emptyElement.Attribute("LfhSize")!.Value);
        Assert.Empty(emptyElement.Elements());
    }

    [Fact]
    public void BlockMapWriter_ReadBlock_CapsOversizedPooledBuffersAtBlockSize()
    {
        byte[] content = Enumerable.Range(0, BlockMap.BlockSize + 17)
            .Select(static value => (byte)(value % 251))
            .ToArray();
        byte[] oversizedBuffer = new byte[BlockMap.BlockSize + 4096];
        using var source = new MemoryStream(content);
        MethodInfo readBlock = typeof(BlockMapWriter).GetMethod("ReadBlock", BindingFlags.NonPublic | BindingFlags.Static)!;

        int read = (int)readBlock.Invoke(null, [source, oversizedBuffer])!;

        Assert.Equal(BlockMap.BlockSize, read);
        Assert.Equal(BlockMap.BlockSize, source.Position);
        Assert.Equal(content.AsSpan(0, BlockMap.BlockSize).ToArray(), oversizedBuffer.AsSpan(0, read).ToArray());
    }

    [Fact]
    public void Crc32Calculator_KnownAnswerAndScalarReference_AreEquivalent()
    {
        byte[] known = "123456789"u8.ToArray();
        var knownCalculator = new Crc32Calculator();
        knownCalculator.Append(known);
        Assert.Equal(0xCBF43926U, knownCalculator.Value);

        int[] sizes = [0, 1, 2, 3, 7, 63, 64, 255, 1024, BlockMap.BlockSize - 1, BlockMap.BlockSize, BlockMap.BlockSize + 1, (BlockMap.BlockSize * 2) + 123];
        foreach (int size in sizes)
        {
            byte[] content = Enumerable.Range(0, size)
                .Select(static value => (byte)((value * 31) ^ (value >> 3)))
                .ToArray();

            var singleAppend = new Crc32Calculator();
            singleAppend.Append(content);
            Assert.Equal(ComputeCrc32(content), singleAppend.Value);

            var chunked = new Crc32Calculator();
            int offset = 0;
            int chunkSize = 1;
            while (offset < content.Length)
            {
                int length = Math.Min(chunkSize, content.Length - offset);
                chunked.Append(content.AsSpan(offset, length));
                offset += length;
                chunkSize = (chunkSize * 17) % 4096 + 1;
            }

            Assert.Equal(singleAppend.Value, chunked.Value);
        }
    }

    [Fact]
    public void StoredZipWriter_WriteByte_UpdatesCrc32()
    {
        using var output = new MemoryStream();
        using (var writer = new StoredZipWriter(output))
        {
            writer.AddEntry(
                "kat.txt",
                stream =>
                {
                    foreach (byte value in "123456789"u8)
                    {
                        stream.WriteByte(value);
                    }
                });
        }

        string packagePath = Path.Combine(_root, "write-byte.zip");
        File.WriteAllBytes(packagePath, output.ToArray());
        LocalHeader header = ReadLocalHeaders(packagePath)["kat.txt"];
        var spanCalculator = new Crc32Calculator();
        spanCalculator.Append("123456789"u8);

        Assert.Equal(0xCBF43926U, header.Crc32);
        Assert.Equal(spanCalculator.Value, header.Crc32);
    }

    [Fact]
    public void BlockMapWriter_CompressedBlocks_EmitsMakeAppxSizes()
    {
        byte[] content = Enumerable.Repeat((byte)'A', BlockMap.BlockSize + 123).ToArray();
        using var compressed = new MemoryStream();

        CompressedBlockMapFile result = BlockMapWriter.CompressAndHash(
            "data.bin",
            new MemoryStream(content),
            compressed,
            CompressionLevel.Optimal);

        Assert.Equal([84L, 10L], result.File.Blocks.Select(static block => block.CompressedSize));
        Assert.Equal(96U, result.CompressedSize);
        Assert.Equal([0x03, 0x00], compressed.ToArray()[^2..]);

        XDocument document = LoadXml(BlockMapWriter.Write(
            [new AuthoredBlockMapFile(result.File, 38)]));
        Assert.Equal(
            ["84", "10"],
            document.Root!.Descendants()
                .Where(static element => element.Name.LocalName == "Block")
                .Select(static element => element.Attribute("Size")!.Value));
    }

    [Theory]
    [InlineData("plain.txt", "plain.txt")]
    [InlineData("space name.txt", "space%20name.txt")]
    [InlineData("!+#%{}^`@&", "%21%2B%23%25%7B%7D%5E%60%40%26")]
    [InlineData("[Content_Types].old", "%5BContent_Types%5D.old")]
    [InlineData("é.txt", "%C3%A9.txt")]
    [InlineData("漢字.txt", "%E6%BC%A2%E5%AD%97.txt")]
    [InlineData("😀.txt", "%F0%9F%98%80.txt")]
    [InlineData("folder/a b.txt", "folder/a%20b.txt")]
    public void OpcPartNameEncoder_EncodesMakeAppxReservedCharacters(string input, string expected)
    {
        Assert.Equal(expected, OpcPartNameEncoder.Encode(input));
    }

    [Theory]
    [InlineData("Data/é.txt", "Data/%C3%A9.txt")]
    [InlineData("Data/漢字.txt", "Data/%E6%BC%A2%E5%AD%97.txt")]
    [InlineData("Data/😀.txt", "Data/%F0%9F%98%80.txt")]
    public void Build_NonAsciiPartNames_UseUtf8PercentEncodingAndRoundTrip(string logicalName, string encodedName)
    {
        string source = CreateSource("non-ascii-" + Guid.NewGuid().ToString("N"));
        byte[] expectedContent = Encoding.UTF8.GetBytes("payload for " + logicalName);
        WritePayload(source, logicalName, expectedContent);
        string output = Path.Combine(_root, Path.GetFileNameWithoutExtension(logicalName) + ".msix");

        MsixPackageBuilder.Build(source, output);

        using (ZipArchive zip = ZipFile.OpenRead(output))
        {
            Assert.Contains(zip.Entries, entry => entry.FullName == encodedName);
            Assert.DoesNotContain(zip.Entries, entry => entry.FullName == logicalName);
        }

        using MsixPackage package = MsixPackage.Open(output);
        Assert.True(package.VerifyBlockMap().IsValid);
        using (Stream payload = package.Opc.OpenPart(logicalName))
        using (var copy = new MemoryStream())
        {
            payload.CopyTo(copy);
            Assert.Equal(expectedContent, copy.ToArray());
        }

        BlockMapFile blockMapFile = package.BlockMap.Files.Single(file => file.Name == logicalName);
        Assert.DoesNotContain('%', blockMapFile.Name);
        Dictionary<string, LocalHeader> headers = ReadLocalHeaders(output);
        Assert.Equal(30 + Encoding.UTF8.GetByteCount(encodedName), headers[logicalName].Size);

        using Stream blockMapStream = package.Opc.OpenPart(OpcPartNames.AppxBlockMap);
        XDocument blockMap = XDocument.Load(blockMapStream);
        XElement fileElement = blockMap.Root!.Elements()
            .Single(element => element.Name.LocalName == "File"
                && element.Attribute("Name")!.Value.Replace('\\', '/') == logicalName);
        Assert.DoesNotContain('%', fileElement.Attribute("Name")!.Value);
        Assert.Equal(
            headers[logicalName].Size.ToString(System.Globalization.CultureInfo.InvariantCulture),
            fileElement.Attribute("LfhSize")!.Value);
    }

    [Fact]
    public void ContentTypesWriter_DerivesDefaultsAndRequiredOverrides()
    {
        XDocument document = LoadXml(ContentTypesWriter.Write(
            ["AppxManifest.xml", "Assets/logo.png", "data.json", "tools/app.exe", "LICENSE"]));
        XElement root = document.Root!;
        Dictionary<string, string> defaults = root.Elements()
            .Where(static element => element.Name.LocalName == "Default")
            .ToDictionary(
                static element => element.Attribute("Extension")!.Value,
                static element => element.Attribute("ContentType")!.Value,
                StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> overrides = root.Elements()
            .Where(static element => element.Name.LocalName == "Override")
            .ToDictionary(
                static element => element.Attribute("PartName")!.Value,
                static element => element.Attribute("ContentType")!.Value,
                StringComparer.Ordinal);

        Assert.Equal("image/png", defaults["png"]);
        Assert.Equal("application/json", defaults["json"]);
        Assert.Equal("application/octet-stream", defaults["exe"]);
        Assert.Equal("application/xml", defaults["xml"]);
        Assert.Equal("application/octet-stream", overrides["/LICENSE"]);
        Assert.Equal("application/vnd.ms-appx.manifest+xml", overrides["/AppxManifest.xml"]);
        Assert.Equal("application/vnd.ms-appx.blockmap+xml", overrides["/AppxBlockMap.xml"]);
    }

    [Fact]
    public void Build_ExcludesInputFootprintsAndGeneratesReplacements()
    {
        string source = CreateSource("footprints");
        File.WriteAllText(Path.Combine(source, OpcPartNames.AppxBlockMap), "stale block map");
        File.WriteAllText(Path.Combine(source, OpcPartNames.AppxSignature), "stale signature");
        File.WriteAllText(Path.Combine(source, OpcPartNames.ContentTypes), "stale content types");
        string output = Path.Combine(_root, "footprints.msix");

        PackResult result = MsixPackageBuilder.Build(source, output);

        using MsixPackage package = MsixPackage.Open(output);
        Assert.True(package.VerifyBlockMap().IsValid);
        Assert.False(package.Opc.ContainsPart(OpcPartNames.AppxSignature));
        Assert.True(package.Opc.ContainsPart(OpcPartNames.AppxBlockMap));
        Assert.True(package.Opc.ContainsPart(OpcPartNames.ContentTypes));
        Assert.DoesNotContain(
            package.BlockMap.Files,
            static file => file.Name is OpcPartNames.AppxBlockMap or OpcPartNames.AppxSignature or OpcPartNames.ContentTypes);
        Assert.Equal(2, result.FileCount);
    }

    [Fact]
    public void Build_GeneratedContentTypesFootprint_UsesLiteralRawZipName()
    {
        string source = CreateSource("raw-content-types");
        string output = Path.Combine(_root, "raw-content-types.msix");

        MsixPackageBuilder.Build(source, output);

        using ZipArchive zip = ZipFile.OpenRead(output);
        Assert.Contains(zip.Entries, static entry => entry.FullName == OpcPartNames.ContentTypes);
        Assert.DoesNotContain(zip.Entries, static entry => entry.FullName == "%5BContent_Types%5D.xml");
    }

    [Fact]
    public void Build_RoundTripsIdentityIntegrityAndPayload()
    {
        string source = CreateSource("roundtrip");
        byte[] large = Enumerable.Range(0, BlockMap.BlockSize + 123)
            .Select(static value => (byte)(value % 251))
            .ToArray();
        byte[] nested = "nested payload"u8.ToArray();
        byte[] reserved = "encoded part name"u8.ToArray();
        WritePayload(source, "Data/large.bin", large);
        WritePayload(source, "Data/nested/value.txt", nested);
        WritePayload(source, "Data/space !+#%.txt", reserved);
        string output = Path.Combine(_root, "roundtrip.msix");

        PackResult result = MsixPackageBuilder.Build(source, output);

        using MsixPackage package = MsixPackage.Open(output);
        Assert.True(package.VerifyBlockMap().IsValid);
        Assert.Equal("Contoso.Authored", package.Identity.Name);
        Assert.Equal(new Version(2, 3, 4, 5), package.Identity.Version);
        Assert.Equal(package.Identity, result.Identity);
        Assert.Equal(5, result.FileCount);
        Assert.Equal(Path.GetFullPath(output), result.OutputPath);

        string extracted = Path.Combine(_root, "extracted");
        PackageExtractor.Extract(package.Opc, extracted);
        Assert.Equal(File.ReadAllBytes(Path.Combine(source, "AppxManifest.xml")), File.ReadAllBytes(Path.Combine(extracted, "AppxManifest.xml")));
        Assert.Equal(large, File.ReadAllBytes(Path.Combine(extracted, "Data", "large.bin")));
        Assert.Equal(nested, File.ReadAllBytes(Path.Combine(extracted, "Data", "nested", "value.txt")));
        Assert.Equal(reserved, File.ReadAllBytes(Path.Combine(extracted, "Data", "space !+#%.txt")));
        Assert.Empty(File.ReadAllBytes(Path.Combine(extracted, "empty.dat")));
    }

    [Fact]
    public void ProgrammaticBuilder_CopiesStreamsAndBuildsWithoutStagingDirectory()
    {
        using var manifest = new MemoryStream(Encoding.UTF8.GetBytes(Manifest));
        using var payload = new MemoryStream("programmatic"u8.ToArray());
        var builder = new MsixPackageBuilder()
            .SetManifest(manifest)
            .AddFile("content/data.txt", payload);
        manifest.Dispose();
        payload.Dispose();
        string output = Path.Combine(_root, "programmatic.msix");

        PackResult result = builder.Build(output);

        using MsixPackage package = MsixPackage.Open(output);
        Assert.True(package.VerifyBlockMap().IsValid);
        Assert.Equal(2, result.FileCount);
        using Stream content = package.Opc.OpenPart("content/data.txt");
        using var reader = new StreamReader(content, Encoding.UTF8);
        Assert.Equal("programmatic", reader.ReadToEnd());
    }

    [Fact]
    public void ProgrammaticBuilder_RejectsTraversalAndGeneratedFootprints()
    {
        var builder = new MsixPackageBuilder();

        Assert.Throws<ArgumentException>(() => builder.AddFile("../escape.txt", Stream.Null));
        Assert.Throws<ArgumentException>(() => builder.AddFile(OpcPartNames.AppxBlockMap, Stream.Null));
        Assert.Throws<ArgumentException>(() => builder.AddFile(OpcPartNames.AppxSignature, Stream.Null));
        Assert.Throws<ArgumentException>(() => builder.AddFile(OpcPartNames.ContentTypes, Stream.Null));
    }

    [Theory]
    [InlineData(@"C:\outside.txt")]
    [InlineData("C:/outside.txt")]
    [InlineData("folder/C:/outside.txt")]
    [InlineData(@"\\server\share\outside.txt")]
    [InlineData("/outside.txt")]
    [InlineData("../outside.txt")]
    [InlineData("folder/../outside.txt")]
    public void ProgrammaticBuilder_RejectsRootedDriveAndTraversalPaths(string packagePath)
    {
        var builder = new MsixPackageBuilder();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => builder.AddFile(packagePath, Stream.Null));

        Assert.Contains("package-relative", exception.Message);
    }

    [Fact]
    public void Build_EmitsStoredEntriesWithExactSchemaValidLocalHeaderSizes()
    {
        string source = CreateSource("local-headers");
        WritePayload(source, "Data/space name.bin", Enumerable.Range(0, 1000).Select(static value => (byte)value).ToArray());
        string output = Path.Combine(_root, "local-headers.msix");

        MsixPackageBuilder.Build(source, output);

        Dictionary<string, LocalHeader> headers = ReadLocalHeaders(output);
        Assert.All(headers.Values, static header => Assert.Equal(0, header.CompressionMethod));

        using MsixPackage package = MsixPackage.Open(output);
        using Stream blockMapStream = package.Opc.OpenPart(OpcPartNames.AppxBlockMap);
        XDocument blockMap = XDocument.Load(blockMapStream);
        foreach (XElement file in blockMap.Root!.Elements().Where(static element => element.Name.LocalName == "File"))
        {
            string name = file.Attribute("Name")!.Value.Replace('\\', '/');
            int declared = int.Parse(file.Attribute("LfhSize")!.Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(declared, 30, 65536);
            Assert.Equal(headers[name].Size, declared);
            Assert.All(file.Elements(), static block => Assert.Null(block.Attribute("Size")));
        }
    }

    [Fact]
    public void Build_RejectsUnsupportedCompressionLevel()
    {
        string source = CreateSource("invalid-compression");

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => MsixPackageBuilder.Build(
                source,
                Path.Combine(_root, "invalid-compression.msix"),
                new PackOptions { CompressionLevel = CompressionLevel.Fastest }));

        Assert.Contains("CompressionLevel.Optimal", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_BlockDeflate_RoundTripsIndependentBlocksAndPreservesLfhSize()
    {
        string source = CreateSource("block-deflate");
        byte[] compressible = Enumerable.Repeat((byte)'A', (BlockMap.BlockSize * 2) + 123).ToArray();
        byte[] incompressible = new byte[BlockMap.BlockSize + 123];
        new Random(41).NextBytes(incompressible);
        byte[] png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        WritePayload(source, "Data/compressible.bin", compressible);
        WritePayload(source, "Data/incompressible.bin", incompressible);
        WritePayload(source, "Assets/logo.png", png);
        string output = Path.Combine(_root, "block-deflate.msix");

        PackResult result = MsixPackageBuilder.Build(
            source,
            output,
            new PackOptions { CompressionLevel = CompressionLevel.Optimal });

        Assert.Equal(CompressionLevel.Optimal, result.CompressionLevel);
        Dictionary<string, LocalHeader> headers = ReadLocalHeaders(output);
        Assert.Equal(8, headers["Data/compressible.bin"].CompressionMethod);
        Assert.Equal(8, headers["Data/incompressible.bin"].CompressionMethod);
        Assert.Equal(8, headers["empty.dat"].CompressionMethod);
        Assert.Equal(0, headers["Assets/logo.png"].CompressionMethod);
        Assert.Equal(2U, headers["empty.dat"].CompressedSize);

        using MsixPackage package = MsixPackage.Open(output);
        Assert.True(package.VerifyBlockMap().IsValid);
        BlockMapFile compressedFile = package.BlockMap.Files.Single(
            static file => file.Name == "Data/compressible.bin");
        BlockMapFile incompressibleFile = package.BlockMap.Files.Single(
            static file => file.Name == "Data/incompressible.bin");
        BlockMapFile pngFile = package.BlockMap.Files.Single(
            static file => file.Name == "Assets/logo.png");
        Assert.Equal([84L, 84L, 10L], compressedFile.Blocks.Select(static block => block.CompressedSize));
        Assert.True(incompressibleFile.Blocks[0].CompressedSize > BlockMap.BlockSize);
        Assert.All(incompressibleFile.Blocks, static block => Assert.NotNull(block.CompressedSize));
        Assert.All(pngFile.Blocks, static block => Assert.Null(block.CompressedSize));

        foreach (BlockMapFile file in package.BlockMap.Files)
        {
            LocalHeader header = headers[file.Name];
            Assert.Equal(30 + Encoding.UTF8.GetByteCount(OpcPartNameEncoder.Encode(file.Name)), header.Size);
            if (header.CompressionMethod == 8)
            {
                Assert.Equal(
                    header.CompressedSize,
                    checked((uint)(file.Blocks.Sum(static block => block.CompressedSize!.Value) + 2)));
                AssertIndependentBlocks(output, header, file);
            }
        }

        using Stream payload = package.Opc.OpenPart("Data/incompressible.bin");
        using var copy = new MemoryStream();
        payload.CopyTo(copy);
        Assert.Equal(incompressible, copy.ToArray());
    }

    [Fact]
    public void Build_BlockDeflate_ExactBlockMultiplesHaveNoTrailingEmptyBlock()
    {
        string source = CreateSource("exact-block-multiples");
        PrepareMakeAppxCompatibleSource(source);
        byte[] oneBlockCompressible = Enumerable.Repeat((byte)'A', BlockMap.BlockSize).ToArray();
        byte[] twoBlocksIncompressible = new byte[BlockMap.BlockSize * 2];
        new Random(41).NextBytes(twoBlocksIncompressible);
        WritePayload(source, "Data/exact-one.bin", oneBlockCompressible);
        WritePayload(source, "Data/exact-two.bin", twoBlocksIncompressible);
        string output = Path.Combine(_root, "exact-block-multiples.msix");

        MsixPackageBuilder.Build(
            source,
            output,
            new PackOptions { CompressionLevel = CompressionLevel.Optimal });

        Dictionary<string, LocalHeader> headers = ReadLocalHeaders(output);
        using (MsixPackage package = MsixPackage.Open(output))
        {
            Assert.True(package.VerifyBlockMap().IsValid);
            AssertExactBlockMultiple(
                output,
                package.BlockMap.Files.Single(static file => file.Name == "Data/exact-one.bin"),
                headers["Data/exact-one.bin"],
                oneBlockCompressible,
                expectedBlockCount: 1);
            AssertExactBlockMultiple(
                output,
                package.BlockMap.Files.Single(static file => file.Name == "Data/exact-two.bin"),
                headers["Data/exact-two.bin"],
                twoBlocksIncompressible,
                expectedBlockCount: 2);
        }

        string? makeAppx = FindMakeAppx();
        if (makeAppx is not null)
        {
            string unpacked = Path.Combine(_root, "exact-block-multiples-unpacked");
            (int exitCode, string diagnostics) = RunProcess(
                makeAppx,
                "unpack", "/o", "/p", output, "/d", unpacked);
            Assert.True(exitCode == 0, diagnostics);
            Assert.Equal(oneBlockCompressible, File.ReadAllBytes(Path.Combine(unpacked, "Data", "exact-one.bin")));
            Assert.Equal(twoBlocksIncompressible, File.ReadAllBytes(Path.Combine(unpacked, "Data", "exact-two.bin")));
        }
    }

    [Fact]
    public void Build_BlockDeflate_IsByteDeterministic()
    {
        string source = CreateSource("deflate-determinism");
        WritePayload(
            source,
            "Data/value.bin",
            Enumerable.Range(0, BlockMap.BlockSize + 17).Select(static value => (byte)(value % 251)).ToArray());
        string first = Path.Combine(_root, "deflate-first.msix");
        string second = Path.Combine(_root, "deflate-second.msix");
        var options = new PackOptions { CompressionLevel = CompressionLevel.Optimal };

        MsixPackageBuilder.Build(source, first, options);
        MsixPackageBuilder.Build(source, second, options);

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
    }

    [Theory]
    [InlineData(CompressionLevel.NoCompression, "stored")]
    [InlineData(CompressionLevel.Optimal, "optimal")]
    public void Build_StoredAndOptimalOutputs_AreDeterministicAndVerify(CompressionLevel compressionLevel, string name)
    {
        string source = CreateSource(name + "-deterministic-verifiable");
        WritePayload(
            source,
            "Data/multi-block.bin",
            Enumerable.Range(0, (BlockMap.BlockSize * 2) + 257).Select(static value => (byte)(value % 241)).ToArray());
        WritePayload(source, "Data/text.txt", "payload for deterministic package authoring"u8.ToArray());
        WritePayload(
            source,
            "Assets/logo.png",
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="));
        string first = Path.Combine(_root, name + "-first.msix");
        string second = Path.Combine(_root, name + "-second.msix");
        var options = new PackOptions { CompressionLevel = compressionLevel };

        MsixPackageBuilder.Build(source, first, options);
        MsixPackageBuilder.Build(source, second, options);

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        using MsixPackage firstPackage = MsixPackage.Open(first);
        using MsixPackage secondPackage = MsixPackage.Open(second);
        Assert.True(firstPackage.VerifyBlockMap().IsValid);
        Assert.True(secondPackage.VerifyBlockMap().IsValid);
        Assert.True(BlockMapVerifier.Verify(firstPackage.Opc, firstPackage.BlockMap).IsValid);
        Assert.True(BlockMapVerifier.Verify(secondPackage.Opc, secondPackage.BlockMap).IsValid);
    }

    [Theory]
    [InlineData(CompressionLevel.NoCompression, "stored.msix")]
    [InlineData(CompressionLevel.Optimal, "optimal.msix")]
    public void Build_OutputMatchesOriginMainGoldenBytes(CompressionLevel compressionLevel, string goldenFile)
    {
        string source = CreateGoldenBaselineSource("golden-" + Path.GetFileNameWithoutExtension(goldenFile));
        string output = Path.Combine(_root, goldenFile);

        MsixPackageBuilder.Build(source, output, new PackOptions { CompressionLevel = compressionLevel });

        byte[] actual = File.ReadAllBytes(output);
        byte[] expected = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "AuthoringGolden",
            goldenFile));
        Assert.Equal(expected, actual);
        using MsixPackage package = MsixPackage.Open(output);
        Assert.True(package.VerifyBlockMap().IsValid);
    }

    [Fact]
    public void Build_MissingRootManifest_ThrowsClearError()
    {
        string source = Path.Combine(_root, "missing-manifest");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "payload.txt"), "payload");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => MsixPackageBuilder.Build(source, Path.Combine(_root, "missing.msix")));

        Assert.Contains("AppxManifest.xml", exception.Message);
    }

    [Fact]
    public void Build_ExistingOutput_RequiresOverwrite()
    {
        string source = CreateSource("overwrite");
        string output = Path.Combine(_root, "overwrite.msix");
        File.WriteAllText(output, "existing");

        Assert.Throws<IOException>(() => MsixPackageBuilder.Build(source, output));
        PackResult result = MsixPackageBuilder.Build(source, output, new PackOptions { Overwrite = true });

        Assert.Equal(Path.GetFullPath(output), result.OutputPath);
        using MsixPackage package = MsixPackage.Open(output);
        Assert.True(package.VerifyBlockMap().IsValid);
    }

    [Fact]
    public void Build_WhenMakeAppxIsAvailable_MatchesHashesAndUnpacks()
    {
        string? makeAppx = FindMakeAppx();
        if (makeAppx is null)
        {
            return;
        }

        string source = CreateSource("makeappx");
        PrepareMakeAppxCompatibleSource(source);
        WritePayload(
            source,
            "Data/sample.bin",
            Enumerable.Range(0, BlockMap.BlockSize + 41).Select(static value => (byte)(value % 239)).ToArray());
        string authoredOutput = Path.Combine(_root, "authored.msix");
        string makeAppxOutput = Path.Combine(_root, "makeappx.msix");
        string unpackedOutput = Path.Combine(_root, "makeappx-unpacked");
        MsixPackageBuilder.Build(
            source,
            authoredOutput,
            new PackOptions { CompressionLevel = CompressionLevel.Optimal });

        (int packExitCode, string packDiagnostics) = RunProcess(
            makeAppx,
            "pack", "/nv", "/o", "/d", source, "/p", makeAppxOutput);
        Assert.True(packExitCode == 0, packDiagnostics);

        using MsixPackage authored = MsixPackage.Open(authoredOutput);
        using MsixPackage reference = MsixPackage.Open(makeAppxOutput);
        Dictionary<string, string[]> authoredHashes = authored.BlockMap.Files.ToDictionary(
            static file => file.Name,
            static file => file.Blocks.Select(static block => block.Hash).ToArray(),
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string[]> referenceHashes = reference.BlockMap.Files.ToDictionary(
            static file => file.Name,
            static file => file.Blocks.Select(static block => block.Hash).ToArray(),
            StringComparer.OrdinalIgnoreCase);

        Assert.Equal(referenceHashes.Keys.Order(StringComparer.OrdinalIgnoreCase), authoredHashes.Keys.Order(StringComparer.OrdinalIgnoreCase));
        foreach ((string name, string[] hashes) in referenceHashes)
        {
            Assert.Equal(hashes, authoredHashes[name]);
        }

        Dictionary<string, long?[]> authoredSizes = authored.BlockMap.Files.ToDictionary(
            static file => file.Name,
            static file => file.Blocks.Select(static block => block.CompressedSize).ToArray(),
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, long?[]> referenceSizes = reference.BlockMap.Files.ToDictionary(
            static file => file.Name,
            static file => file.Blocks.Select(static block => block.CompressedSize).ToArray(),
            StringComparer.OrdinalIgnoreCase);
        foreach ((string name, long?[] sizes) in referenceSizes)
        {
            Assert.Equal(sizes, authoredSizes[name]);
        }

        Assert.True(authored.VerifyBlockMap().IsValid);

        (int unpackExitCode, string unpackDiagnostics) = RunProcess(
            makeAppx,
            "unpack", "/o", "/p", authoredOutput, "/d", unpackedOutput);
        Assert.True(unpackExitCode == 0, unpackDiagnostics);
        foreach (string sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(source, sourceFile);
            Assert.Equal(
                File.ReadAllBytes(sourceFile),
                File.ReadAllBytes(Path.Combine(unpackedOutput, relativePath)));
        }
    }

    private static void PrepareMakeAppxCompatibleSource(string source)
    {
        File.WriteAllText(
            Path.Combine(source, OpcPartNames.AppxManifest),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                     xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
                     xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
                     IgnorableNamespaces="uap rescap">
              <Identity Name="Contoso.Authored" Publisher="CN=Contoso" Version="2.3.4.5" ProcessorArchitecture="x64" />
              <Properties>
                <DisplayName>Authored package</DisplayName>
                <PublisherDisplayName>Contoso Ltd</PublisherDisplayName>
                <Logo>Assets\StoreLogo.png</Logo>
              </Properties>
              <Dependencies>
                <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.26100.0" />
              </Dependencies>
              <Resources>
                <Resource Language="en-us" />
              </Resources>
              <Applications>
                <Application Id="App" Executable="app.exe" EntryPoint="Windows.FullTrustApplication">
                  <uap:VisualElements DisplayName="Authored package" Description="Differential fixture"
                    BackgroundColor="transparent" Square150x150Logo="Assets\Square150x150Logo.png"
                    Square44x44Logo="Assets\Square44x44Logo.png" />
                </Application>
              </Applications>
              <Capabilities>
                <rescap:Capability Name="runFullTrust" />
              </Capabilities>
            </Package>
            """,
            new UTF8Encoding(false));
        byte[] png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        WritePayload(source, "Assets/StoreLogo.png", png);
        WritePayload(source, "Assets/Square150x150Logo.png", png);
        WritePayload(source, "Assets/Square44x44Logo.png", png);
        WritePayload(source, "app.exe", []);
    }

    private string CreateSource(string name)
    {
        string source = Path.Combine(_root, name);
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "AppxManifest.xml"), Manifest, new UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(source, "empty.dat"), []);
        return source;
    }

    private string CreateGoldenBaselineSource(string name)
    {
        string source = CreateSource(name);
        File.WriteAllText(
            Path.Combine(source, OpcPartNames.AppxManifest),
            Manifest.ReplaceLineEndings("\n"),
            new UTF8Encoding(false));
        WritePayload(source, "Data/payload.txt", "golden baseline payload"u8.ToArray());
        WritePayload(
            source,
            "Data/pattern.bin",
            Enumerable.Range(0, 4097).Select(static value => (byte)((value * 13) % 251)).ToArray());
        WritePayload(
            source,
            "Assets/logo.png",
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="));
        return source;
    }

    private static void WritePayload(string root, string relativePath, byte[] content)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    private static XDocument LoadXml(byte[] content)
    {
        using var stream = new MemoryStream(content);
        return XDocument.Load(stream);
    }

    private static string? FindMakeAppx()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        string? programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string kitsBin = Path.Combine(programFilesX86, "Windows Kits", "10", "bin");
        if (!Directory.Exists(kitsBin))
        {
            return null;
        }

        string[] candidates = Directory.EnumerateFiles(kitsBin, "makeappx.exe", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return candidates.LastOrDefault(static path => path.Contains(
                $"{Path.DirectorySeparatorChar}arm64{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            ?? candidates.LastOrDefault(static path => path.Contains(
                $"{Path.DirectorySeparatorChar}x64{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase));
    }

    private static (int ExitCode, string Diagnostics) RunProcess(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (
            process.ExitCode,
            standardOutput.GetAwaiter().GetResult() + standardError.GetAwaiter().GetResult());
    }

    private static Dictionary<string, LocalHeader> ReadLocalHeaders(string packagePath)
    {
        byte[] package = File.ReadAllBytes(packagePath);
        var headers = new Dictionary<string, LocalHeader>(StringComparer.OrdinalIgnoreCase);
        int offset = 0;
        while (offset + 4 <= package.Length
            && BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(offset, 4)) == 0x04034B50)
        {
            ushort compressionMethod = BinaryPrimitives.ReadUInt16LittleEndian(package.AsSpan(offset + 8, 2));
            uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(offset + 18, 4));
            uint crc32 = BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(offset + 14, 4));
            ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(package.AsSpan(offset + 26, 2));
            ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(package.AsSpan(offset + 28, 2));
            int localHeaderSize = 30 + nameLength + extraLength;
            string rawName = Encoding.UTF8.GetString(package, offset + 30, nameLength);
            Assert.True(OpcPackage.TryCanonicalizePartName(rawName, out string name));
            uint uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(offset + 22, 4));
            headers.Add(name, new LocalHeader(
                localHeaderSize,
                compressionMethod,
                crc32,
                compressedSize,
                uncompressedSize,
                offset + localHeaderSize));
            offset = checked(offset + localHeaderSize + (int)compressedSize);
        }

        return headers;
    }

    private static void AssertIndependentBlocks(string packagePath, LocalHeader header, BlockMapFile file)
    {
        byte[] package = File.ReadAllBytes(packagePath);
        int offset = header.DataOffset;
        long remaining = file.Size;
        foreach (BlockMapBlock block in file.Blocks)
        {
            int compressedSize = checked((int)block.CompressedSize!.Value);
            byte[] terminated = new byte[compressedSize + 2];
            package.AsSpan(offset, compressedSize).CopyTo(terminated);
            terminated[^2] = 0x03;
            terminated[^1] = 0x00;
            offset += compressedSize;

            using var compressed = new MemoryStream(terminated);
            using var inflater = new DeflateStream(compressed, CompressionMode.Decompress);
            using var uncompressed = new MemoryStream();
            inflater.CopyTo(uncompressed);
            int expectedSize = checked((int)Math.Min(BlockMap.BlockSize, remaining));
            Assert.Equal(expectedSize, uncompressed.Length);
            Assert.Equal(
                block.Hash,
                Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(uncompressed.ToArray())));
            remaining -= expectedSize;
        }

        Assert.Equal(0, remaining);
        Assert.Equal([0x03, 0x00], package.AsSpan(offset, 2).ToArray());
    }

    private static void AssertExactBlockMultiple(
        string packagePath,
        BlockMapFile file,
        LocalHeader header,
        byte[] expected,
        int expectedBlockCount)
    {
        Assert.Equal(expectedBlockCount, file.Blocks.Count);
        Assert.Equal((long)expectedBlockCount * BlockMap.BlockSize, file.Size);
        Assert.Equal(8, header.CompressionMethod);
        Assert.Equal((uint)expected.Length, header.UncompressedSize);
        Assert.Equal(
            header.CompressedSize,
            checked((uint)(file.Blocks.Sum(static block => block.CompressedSize!.Value) + 2)));
        Assert.Equal(ComputeCrc32(expected), header.Crc32);

        if (expectedBlockCount == 1)
        {
            Assert.True(file.Blocks[0].CompressedSize < BlockMap.BlockSize);
        }
        else
        {
            Assert.All(file.Blocks, static block => Assert.True(block.CompressedSize > BlockMap.BlockSize));
        }

        byte[] package = File.ReadAllBytes(packagePath);
        using var compressed = new MemoryStream(
            package,
            header.DataOffset,
            checked((int)header.CompressedSize),
            writable: false);
        using var inflater = new DeflateStream(compressed, CompressionMode.Decompress);
        using var uncompressed = new MemoryStream();
        inflater.CopyTo(uncompressed);
        Assert.Equal(expected, uncompressed.ToArray());
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xEDB88320);
            }
        }

        return ~crc;
    }

    private sealed record LocalHeader(
        int Size,
        ushort CompressionMethod,
        uint Crc32,
        uint CompressedSize,
        uint UncompressedSize,
        int DataOffset);
}
