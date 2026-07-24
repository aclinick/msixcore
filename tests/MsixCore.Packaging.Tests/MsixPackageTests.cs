using System.IO.Compression;
using MsixCore.Packaging;

namespace MsixCore.Packaging.Tests;

public class MsixPackageTests
{
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
}
