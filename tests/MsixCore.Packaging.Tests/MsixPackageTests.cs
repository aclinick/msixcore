using System.IO.Compression;
using System.Text;
using MsixCore.Packaging;
using MsixCore.Packaging.Authoring;
using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging.Tests;

public class MsixPackageTests
{
    private const string ValidManifest =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                 xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10">
          <Identity Name="Contoso.MyApp" Publisher="CN=Contoso" Version="1.0.0.0" ProcessorArchitecture="x64" />
          <Properties>
            <DisplayName>Contoso My App</DisplayName>
            <PublisherDisplayName>Contoso Ltd</PublisherDisplayName>
            <Logo>Assets\StoreLogo.png</Logo>
          </Properties>
        </Package>
        """;

    private static MemoryStream CreateMinimalPackage()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("AppxManifest.xml").Open().Dispose();
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateValidPackage()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("AppxManifest.xml").Open(), Encoding.UTF8))
            {
                writer.Write(ValidManifest);
            }

            using (var logo = new BinaryWriter(archive.CreateEntry("Assets/StoreLogo.png").Open()))
            {
                logo.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            }
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void Open_ExposesOpcContainer()
    {
        using MemoryStream zip = CreateMinimalPackage();
        using MsixPackage package = MsixPackage.Open(zip, leaveOpen: true);

        Assert.True(package.Opc.ContainsPart("AppxManifest.xml"));
    }

    [Fact]
    public void Opc_AfterDispose_Throws()
    {
        MemoryStream zip = CreateMinimalPackage();
        MsixPackage package = MsixPackage.Open(zip, leaveOpen: true);
        package.Dispose();

        Assert.Throws<ObjectDisposedException>(() => package.Opc);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        using MemoryStream zip = CreateMinimalPackage();
        MsixPackage package = MsixPackage.Open(zip, leaveOpen: true);

        package.Dispose();
        package.Dispose(); // must not throw
    }

    [Fact]
    public void Manifest_BindsIdentityAndProperties()
    {
        using MemoryStream zip = CreateValidPackage();
        using MsixPackage package = MsixPackage.Open(zip, leaveOpen: true);

        Assert.Equal("Contoso.MyApp", package.Identity.Name);
        Assert.Equal(ProcessorArchitecture.X64, package.Identity.Architecture);
        Assert.Equal("Contoso My App", package.DisplayName);
        Assert.Equal("Contoso Ltd", package.PublisherDisplayName);
    }

    [Fact]
    public void OpenLogo_ReturnsLogoStream_ResolvingBackslashPath()
    {
        using MemoryStream zip = CreateValidPackage();
        using MsixPackage package = MsixPackage.Open(zip, leaveOpen: true);

        using Stream? logo = package.OpenLogo();
        Assert.NotNull(logo);
    }

    [Fact]
    public void Manifest_EmptyManifest_Throws()
    {
        using MemoryStream zip = CreateMinimalPackage();
        using MsixPackage package = MsixPackage.Open(zip, leaveOpen: true);

        Assert.Throws<InvalidDataException>(() => package.Manifest);
    }

    [Fact]
    public void VerifyBlockMap_MatchingPackage_IsValid()
    {
        var payload = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["AppxManifest.xml"] = Encoding.UTF8.GetBytes(ValidManifest),
            ["Assets/StoreLogo.png"] = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A },
        };

        var allParts = new Dictionary<string, byte[]>(payload, StringComparer.Ordinal)
        {
            ["AppxBlockMap.xml"] = Encoding.UTF8.GetBytes(PackageBuilder.BlockMapXml(payload)),
            [OpcPartNames.ContentTypes] = ContentTypesWriter.Write(payload.Keys),
        };

        using var zip = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(zip, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] content) in allParts)
            {
                using Stream entry = archive.CreateEntry(name, CompressionLevel.NoCompression).Open();
                entry.Write(content);
            }
        }

        zip.Position = 0;
        using MsixPackage package = MsixPackage.Open(zip, leaveOpen: true);

        Assert.True(package.VerifyBlockMap().IsValid);
    }

    [Fact]
    public void VerifyBlockMap_NoBlockMap_Throws()
    {
        using MemoryStream zip = CreateMinimalPackage();
        using MsixPackage package = MsixPackage.Open(zip, leaveOpen: true);

        Assert.Throws<InvalidDataException>(() => package.VerifyBlockMap());
    }

    [Fact]
    public void Signature_UnsignedPackage_IsNull()
    {
        using MemoryStream zip = CreateMinimalPackage();
        using MsixPackage package = MsixPackage.Open(zip, leaveOpen: true);

        Assert.False(package.IsSigned);
        Assert.Null(package.ReadSignature());
    }
}
