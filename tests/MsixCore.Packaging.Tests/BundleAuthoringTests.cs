using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using MsixCore.Packaging.Authoring;
using MsixCore.Packaging.Integrity;
using MsixCore.Packaging.Manifest;
using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging.Tests;

public sealed class BundleAuthoringTests : IDisposable
{
    private readonly string _root;

    public BundleAuthoringTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msix-bundle-authoring-" + Guid.NewGuid().ToString("N"));
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
    public void Build_SingleApplication_RoundTripsSchemaOffsetsAndStoredChild()
    {
        string child = CreatePackage("single-x64.msix", ProcessorArchitecture.X64);
        string output = Path.Combine(_root, "single.msixbundle");

        BundleResult result = MsixBundleBuilder.Build(
            [child],
            output,
            new BundleOptions { Version = new Version(4, 3, 2, 1) });

        Assert.Equal(new Version(4, 3, 2, 1), result.Identity.Version);
        BundlePackageEntry entry = Assert.Single(result.Packages);
        Assert.Equal(BundlePackageType.Application, entry.Type);
        Assert.Equal(ProcessorArchitecture.X64, entry.Architecture);
        Assert.Equal(new FileInfo(child).Length, entry.Size);

        Dictionary<string, LocalHeader> headers = ReadLocalHeaders(output);
        LocalHeader childHeader = headers[Path.GetFileName(child)];
        Assert.Equal(0, childHeader.CompressionMethod);
        Assert.Equal(childHeader.DataOffset, entry.Offset);
        Assert.Equal(File.ReadAllBytes(child), ReadBytes(output, childHeader.DataOffset, entry.Size));
        Assert.Equal(8, headers[OpcPartNames.AppxBundleManifest].CompressionMethod);

        using MsixBundle bundle = MsixBundle.Open(output);
        Assert.Equal(result.Identity, bundle.Identity);
        Assert.Equal(PackageShape(entry), PackageShape(Assert.Single(bundle.Packages)));

        using Stream contentTypesStream = bundle.Opc.OpenPart(OpcPartNames.ContentTypes);
        XDocument contentTypes = XDocument.Load(contentTypesStream);
        Assert.Contains(
            contentTypes.Root!.Elements(),
            static element =>
                element.Name.LocalName == "Default"
                && element.Attribute("Extension")?.Value == "msix"
                && element.Attribute("ContentType")?.Value == "application/vnd.ms-appx");

        using Stream blockMapStream = bundle.Opc.OpenPart(OpcPartNames.AppxBlockMap);
        XDocument blockMap = XDocument.Load(blockMapStream);
        XElement mappedFile = Assert.Single(
            blockMap.Root!.Elements(),
            static element => element.Name.LocalName == "File");
        Assert.Equal(
            @"AppxMetadata\AppxBundleManifest.xml",
            mappedFile.Attribute("Name")!.Value);
    }

    [Fact]
    public void Build_MultiArchitecture_IsByteDeterministic()
    {
        string x64 = CreatePackage("app-x64.msix", ProcessorArchitecture.X64);
        string arm64 = CreatePackage("app-arm64.msix", ProcessorArchitecture.Arm64);
        string first = Path.Combine(_root, "first.msixbundle");
        string second = Path.Combine(_root, "second.msixbundle");
        var options = new BundleOptions { Version = new Version(1, 2, 3, 4) };

        MsixBundleBuilder.Build([x64, arm64], first, options);
        MsixBundleBuilder.Build([x64, arm64], second, options);

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        using MsixBundle bundle = MsixBundle.Open(first);
        Assert.Equal(
            [ProcessorArchitecture.X64, ProcessorArchitecture.Arm64],
            bundle.Packages.Select(static package => package.Architecture));
    }

    [Fact]
    public void Build_ApplicationAndResource_WritesMakeAppxPackageShapes()
    {
        string app = CreatePackage("app.msix", ProcessorArchitecture.X64);
        string resource = CreatePackage(
            "resources-en.msix",
            ProcessorArchitecture.Neutral,
            resourceId: "en-US",
            isResourcePackage: true);
        string output = Path.Combine(_root, "resource.msixbundle");

        MsixBundleBuilder.Build([app, resource], output);

        using MsixBundle bundle = MsixBundle.Open(output);
        BundlePackageEntry appEntry = bundle.Packages[0];
        BundlePackageEntry resourceEntry = bundle.Packages[1];
        Assert.Equal(BundlePackageType.Application, appEntry.Type);
        Assert.Equal(BundlePackageType.Resource, resourceEntry.Type);
        Assert.Equal("en-US", resourceEntry.ResourceId);
        Assert.Equal("en-us", Assert.Single(resourceEntry.Resources).Language);
        Assert.Single(resourceEntry.TargetDeviceFamilies);

        using Stream manifestStream = bundle.Opc.OpenPart(OpcPartNames.AppxBundleManifest);
        XDocument manifest = XDocument.Load(manifestStream);
        XElement[] packages = manifest.Root!
            .Elements().Single(static element => element.Name.LocalName == "Packages")
            .Elements().ToArray();
        Assert.NotNull(packages[0].Attribute("Architecture"));
        Assert.Null(packages[0].Attribute("ResourceId"));
        Assert.Null(packages[1].Attribute("Architecture"));
        Assert.Equal("en-US", packages[1].Attribute("ResourceId")!.Value);
        Assert.Equal(
            "http://schemas.microsoft.com/appx/2018/bundle",
            packages[1].Elements().Single(static element => element.Name.LocalName == "Dependencies").Name.NamespaceName);
    }

    [Fact]
    public void Build_DefaultVersionUsesCommonChildVersion()
    {
        string x64 = CreatePackage(
            "app-x64.msix",
            ProcessorArchitecture.X64,
            version: new Version(2, 3, 4, 5));
        string arm64 = CreatePackage(
            "app-arm64.msix",
            ProcessorArchitecture.Arm64,
            version: new Version(2, 3, 4, 5));

        BundleResult result = MsixBundleBuilder.Build(
            [x64, arm64],
            Path.Combine(_root, "version.msixbundle"));

        Assert.Equal(new Version(2, 3, 4, 5), result.Identity.Version);
    }

    [Fact]
    public void Build_MismatchedChildVersions_Throws()
    {
        string older = CreatePackage(
            "older-x64.msix",
            ProcessorArchitecture.X64,
            version: new Version(1, 0, 0, 0));
        string newer = CreatePackage(
            "newer-arm64.msix",
            ProcessorArchitecture.Arm64,
            version: new Version(2, 3, 4, 5));

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            MsixBundleBuilder.Build(
                [older, newer],
                Path.Combine(_root, "mismatch.msixbundle")));

        Assert.Contains("same Version", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_AppxInput_CreatesAppxBundle()
    {
        string child = CreatePackage("sample.appx", ProcessorArchitecture.X64);
        string output = Path.Combine(_root, "sample.appxbundle");

        BundleResult result = MsixBundleBuilder.Build([child], output);

        Assert.Equal(Path.GetFullPath(output), result.OutputPath);
        using MsixBundle bundle = MsixBundle.Open(output);
        Assert.Equal("sample.appx", Assert.Single(bundle.Packages).FileName);
    }

    [Fact]
    public void Build_RejectsNoInputsDuplicateIdentityAndNonPackageInput()
    {
        Assert.Throws<InvalidOperationException>(
            () => new MsixBundleBuilder().Build(Path.Combine(_root, "empty.msixbundle")));

        string package = CreatePackage("duplicate.msix", ProcessorArchitecture.X64);
        Assert.Throws<InvalidDataException>(
            () => MsixBundleBuilder.Build(
                [package, package],
                Path.Combine(_root, "duplicate.msixbundle")));

        string text = Path.Combine(_root, "not-a-package.txt");
        File.WriteAllText(text, "not a package");
        Assert.Throws<ArgumentException>(
            () => MsixBundleBuilder.Build(
                [text],
                Path.Combine(_root, "invalid.msixbundle")));
    }

    [Fact]
    public void Build_RejectsDifferentFamiliesAndDuplicateApplicationArchitecture()
    {
        string first = CreatePackage("first.msix", ProcessorArchitecture.X64);
        string differentFamily = CreatePackage(
            "different.msix",
            ProcessorArchitecture.Arm64,
            name: "Contoso.Other");
        Assert.Throws<InvalidDataException>(
            () => MsixBundleBuilder.Build(
                [first, differentFamily],
                Path.Combine(_root, "families.msixbundle")));

        string secondX64 = CreatePackage(
            "second-x64.msix",
            ProcessorArchitecture.X64);
        Assert.Throws<InvalidDataException>(
            () => MsixBundleBuilder.Build(
                [first, secondX64],
                Path.Combine(_root, "architectures.msixbundle")));
    }

    [Fact]
    public void Build_WhenMakeAppxIsAvailable_MatchesReferenceAndUnbundlesByteForByte()
    {
        string? makeAppx = FindMakeAppx();
        if (makeAppx is null)
        {
            return;
        }

        string x64 = CreatePackage("sample-x64.msix", ProcessorArchitecture.X64);
        string x86 = CreatePackage("sample-x86.msix", ProcessorArchitecture.X86);
        string resource = CreatePackage(
            "sample-resource.msix",
            ProcessorArchitecture.Neutral,
            resourceId: "en-US",
            isResourcePackage: true);
        string inputDirectory = Path.Combine(_root, "reference-input");
        Directory.CreateDirectory(inputDirectory);
        foreach (string package in new[] { x64, x86, resource })
        {
            File.Copy(package, Path.Combine(inputDirectory, Path.GetFileName(package)));
        }

        string authored = Path.Combine(_root, "authored.msixbundle");
        string reference = Path.Combine(_root, "reference.msixbundle");
        MsixBundleBuilder.Build(
            [x64, x86, resource],
            authored,
            new BundleOptions { Version = new Version(1, 0, 0, 0) });

        (int bundleExitCode, string bundleDiagnostics) = RunProcess(
            makeAppx,
            "bundle", "/o", "/bv", "1.0.0.0", "/d", inputDirectory, "/p", reference);
        Assert.True(bundleExitCode == 0, bundleDiagnostics);

        using MsixBundle authoredBundle = MsixBundle.Open(authored);
        using MsixBundle referenceBundle = MsixBundle.Open(reference);
        Assert.Equal(referenceBundle.Identity, authoredBundle.Identity);
        Assert.Equal(
            referenceBundle.Packages.Select(PackageShape),
            authoredBundle.Packages.Select(PackageShape));
        Assert.Equal(
            referenceBundle.Opc.PartNames.Order(StringComparer.OrdinalIgnoreCase),
            authoredBundle.Opc.PartNames.Order(StringComparer.OrdinalIgnoreCase));

        using Stream authoredBlockMapStream = authoredBundle.Opc.OpenPart(OpcPartNames.AppxBlockMap);
        using Stream referenceBlockMapStream = referenceBundle.Opc.OpenPart(OpcPartNames.AppxBlockMap);
        Assert.Equal(
            [OpcPartNames.AppxBundleManifest],
            BlockMapParser.Parse(authoredBlockMapStream).Files.Select(static file => file.Name));
        Assert.Equal(
            [OpcPartNames.AppxBundleManifest],
            BlockMapParser.Parse(referenceBlockMapStream).Files.Select(static file => file.Name));

        using Stream authoredContentTypes = authoredBundle.Opc.OpenPart(OpcPartNames.ContentTypes);
        using Stream referenceContentTypes = referenceBundle.Opc.OpenPart(OpcPartNames.ContentTypes);
        Assert.Equal(
            ReadContentTypes(referenceContentTypes),
            ReadContentTypes(authoredContentTypes));

        Dictionary<string, LocalHeader> authoredHeaders = ReadLocalHeaders(authored);
        Dictionary<string, LocalHeader> referenceHeaders = ReadLocalHeaders(reference);
        foreach (BundlePackageEntry package in authoredBundle.Packages)
        {
            Assert.Equal(0, authoredHeaders[package.FileName].CompressionMethod);
            Assert.Equal(0, referenceHeaders[package.FileName].CompressionMethod);
            Assert.Equal(authoredHeaders[package.FileName].DataOffset, package.Offset);
            Assert.Equal(referenceHeaders[package.FileName].DataOffset,
                referenceBundle.Packages.Single(candidate => candidate.FileName == package.FileName).Offset);
        }

        string unpacked = Path.Combine(_root, "unbundled");
        (int unpackExitCode, string unpackDiagnostics) = RunProcess(
            makeAppx,
            "unbundle", "/o", "/p", authored, "/d", unpacked);
        Assert.True(unpackExitCode == 0, unpackDiagnostics);
        foreach (string package in new[] { x64, x86, resource })
        {
            Assert.Equal(
                SHA256.HashData(File.ReadAllBytes(package)),
                SHA256.HashData(File.ReadAllBytes(Path.Combine(unpacked, Path.GetFileName(package)))));
        }
    }

    private string CreatePackage(
        string fileName,
        ProcessorArchitecture architecture,
        string name = "Contoso.BundleSample",
        Version? version = null,
        string resourceId = "",
        bool isResourcePackage = false)
    {
        version ??= new Version(1, 0, 0, 0);
        string source = Path.Combine(_root, Path.GetFileNameWithoutExtension(fileName));
        Directory.CreateDirectory(source);
        string architectureAttribute = isResourcePackage
            ? string.Empty
            : $" ProcessorArchitecture=\"{PackageIdentity.ArchitectureMoniker(architecture)}\"";
        string resourceIdAttribute = string.IsNullOrEmpty(resourceId)
            ? string.Empty
            : $" ResourceId=\"{resourceId}\"";
        string resourcePackageProperty = isResourcePackage
            ? "<ResourcePackage>true</ResourcePackage>"
            : string.Empty;
        string applications = isResourcePackage
            ? string.Empty
            : """
              <Applications>
                <Application Id="App" Executable="app.exe" EntryPoint="Windows.FullTrustApplication">
                  <uap:VisualElements DisplayName="Bundle Sample" Description="Bundle fixture"
                    BackgroundColor="transparent" Square150x150Logo="Assets\Square150x150Logo.png"
                    Square44x44Logo="Assets\Square44x44Logo.png" />
                </Application>
              </Applications>
              <Capabilities><rescap:Capability Name="runFullTrust" /></Capabilities>
              """;
        string manifest =
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                     xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
                     xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
                     IgnorableNamespaces="uap rescap">
              <Identity Name="{{name}}" Publisher="CN=Contoso" Version="{{version}}"{{architectureAttribute}}{{resourceIdAttribute}} />
              <Properties>
                {{resourcePackageProperty}}
                <DisplayName>Bundle Sample</DisplayName>
                <PublisherDisplayName>Contoso</PublisherDisplayName>
                <Logo>Assets\StoreLogo.png</Logo>
              </Properties>
              <Dependencies>
                <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.26100.0" />
              </Dependencies>
              <Resources><Resource Language="en-us" /></Resources>
              {{applications}}
            </Package>
            """;
        File.WriteAllText(Path.Combine(source, OpcPartNames.AppxManifest), manifest, new UTF8Encoding(false));
        byte[] png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        WriteFile(source, "Assets/StoreLogo.png", png);
        WriteFile(source, "Assets/Square150x150Logo.png", png);
        WriteFile(source, "Assets/Square44x44Logo.png", png);
        if (!isResourcePackage)
        {
            WriteFile(source, "app.exe", []);
        }

        string output = Path.Combine(_root, fileName);
        MsixPackageBuilder.Build(source, output);
        return output;
    }

    private static string PackageShape(BundlePackageEntry package) =>
        string.Join(
            "|",
            package.FileName,
            package.Type,
            package.Version,
            package.Architecture,
            package.ResourceId,
            string.Join(
                ";",
                package.Resources.Select(static resource =>
                    $"{resource.Language},{resource.Scale},{resource.DXFeatureLevel}")),
            string.Join(
                ";",
                package.TargetDeviceFamilies.Select(static family =>
                    $"{family.Name},{family.MinVersion},{family.MaxVersionTested}")),
            package.Size);

    private static void WriteFile(string root, string relativePath, byte[] content)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    private static string[] ReadContentTypes(Stream stream) =>
        XDocument.Load(stream).Root!.Elements()
            .Select(static element => element.Name.LocalName == "Default"
                ? $"Default:{element.Attribute("Extension")?.Value}:{element.Attribute("ContentType")?.Value}"
                : $"Override:{element.Attribute("PartName")?.Value}:{element.Attribute("ContentType")?.Value}")
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static byte[] ReadBytes(string path, long offset, long count)
    {
        byte[] bytes = new byte[checked((int)count)];
        using FileStream stream = File.OpenRead(path);
        stream.Position = offset;
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static string? FindMakeAppx()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        string kitsBin = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Windows Kits",
            "10",
            "bin");
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
        while (offset + 30 <= package.Length
            && BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(offset, 4)) == 0x04034B50)
        {
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(package.AsSpan(offset + 6, 2));
            ushort compressionMethod = BinaryPrimitives.ReadUInt16LittleEndian(package.AsSpan(offset + 8, 2));
            uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(offset + 18, 4));
            ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(package.AsSpan(offset + 26, 2));
            ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(package.AsSpan(offset + 28, 2));
            int localHeaderSize = 30 + nameLength + extraLength;
            string name = Encoding.UTF8.GetString(package, offset + 30, nameLength);
            if ((flags & 0x0008) != 0)
            {
                using var archive = new ZipArchive(File.OpenRead(packagePath), ZipArchiveMode.Read);
                compressedSize = checked((uint)archive.GetEntry(name)!.CompressedLength);
            }

            headers[name.Replace('\\', '/')] = new LocalHeader(
                compressionMethod,
                offset + localHeaderSize,
                compressedSize);
            offset = checked(offset + localHeaderSize + (int)compressedSize);
            if ((flags & 0x0008) != 0)
            {
                offset += 24;
            }
        }

        return headers;
    }

    private sealed record LocalHeader(
        ushort CompressionMethod,
        int DataOffset,
        uint CompressedSize);
}
