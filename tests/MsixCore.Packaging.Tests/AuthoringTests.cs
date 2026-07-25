using System.Buffers.Binary;
using System.IO.Compression;
using System.Diagnostics;
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

    [Theory]
    [InlineData("plain.txt", "plain.txt")]
    [InlineData("space name.txt", "space%20name.txt")]
    [InlineData("!+#%{}^`@&", "%21%2B%23%25%7B%7D%5E%60%40%26")]
    [InlineData("folder/a b.txt", "folder/a%20b.txt")]
    public void OpcPartNameEncoder_EncodesMakeAppxReservedCharacters(string input, string expected)
    {
        Assert.Equal(expected, OpcPartNameEncoder.Encode(input));
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
    public void Build_RejectsWholeEntryCompression()
    {
        string source = CreateSource("invalid-compression");

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => MsixPackageBuilder.Build(
                source,
                Path.Combine(_root, "invalid-compression.msix"),
                new PackOptions { CompressionLevel = CompressionLevel.Optimal }));

        Assert.Contains("stored", exception.Message, StringComparison.OrdinalIgnoreCase);
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
        WritePayload(
            source,
            "Data/sample.bin",
            Enumerable.Range(0, BlockMap.BlockSize + 41).Select(static value => (byte)(value % 239)).ToArray());
        string authoredOutput = Path.Combine(_root, "authored.msix");
        string makeAppxOutput = Path.Combine(_root, "makeappx.msix");
        string unpackedOutput = Path.Combine(_root, "makeappx-unpacked");
        MsixPackageBuilder.Build(source, authoredOutput);

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

        (int unpackExitCode, string unpackDiagnostics) = RunProcess(
            makeAppx,
            "unpack", "/o", "/p", authoredOutput, "/d", unpackedOutput);
        Assert.True(unpackExitCode == 0, unpackDiagnostics);
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(source, "Data", "sample.bin")),
            File.ReadAllBytes(Path.Combine(unpackedOutput, "Data", "sample.bin")));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(source, OpcPartNames.AppxManifest)),
            File.ReadAllBytes(Path.Combine(unpackedOutput, OpcPartNames.AppxManifest)));
    }

    private string CreateSource(string name)
    {
        string source = Path.Combine(_root, name);
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "AppxManifest.xml"), Manifest, new UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(source, "empty.dat"), []);
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

        return Directory.EnumerateFiles(kitsBin, "makeappx.exe", SearchOption.AllDirectories)
            .Where(static path => path.Contains($"{Path.DirectorySeparatorChar}x64{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .LastOrDefault();
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
            ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(package.AsSpan(offset + 26, 2));
            ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(package.AsSpan(offset + 28, 2));
            int localHeaderSize = 30 + nameLength + extraLength;
            string rawName = Encoding.UTF8.GetString(package, offset + 30, nameLength);
            Assert.True(OpcPackage.TryCanonicalizePartName(rawName, out string name));
            headers.Add(name, new LocalHeader(localHeaderSize, compressionMethod));
            offset = checked(offset + localHeaderSize + (int)compressedSize);
        }

        return headers;
    }

    private sealed record LocalHeader(int Size, ushort CompressionMethod);
}
