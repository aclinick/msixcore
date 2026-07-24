using System.IO.Compression;
using System.Text;
using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging.Tests;

public class OpcPackageTests
{
    private static MemoryStream CreateZip(params (string name, string content)[] parts)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in parts)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name);
                using Stream entryStream = entry.Open();
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                entryStream.Write(bytes, 0, bytes.Length);
            }
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void PartNames_ExcludeDirectories()
    {
        using MemoryStream zip = CreateZip(
            ("AppxManifest.xml", "<manifest/>"),
            ("VFS/ProgramFilesX64/app.exe", "MZ"));
        using OpcPackage package = OpcPackage.Open(zip);

        Assert.Contains("AppxManifest.xml", package.PartNames);
        Assert.Contains("VFS/ProgramFilesX64/app.exe", package.PartNames);
        Assert.DoesNotContain(package.PartNames, p => p.EndsWith('/'));
    }

    [Fact]
    public void ContainsPart_IsCaseInsensitive()
    {
        using MemoryStream zip = CreateZip(("AppxManifest.xml", "<manifest/>"));
        using OpcPackage package = OpcPackage.Open(zip);

        Assert.True(package.ContainsPart("AppxManifest.xml"));
        Assert.True(package.ContainsPart("appxmanifest.xml"));
        Assert.False(package.ContainsPart("Missing.xml"));
    }

    [Fact]
    public void OpenPart_ReturnsPartContent()
    {
        using MemoryStream zip = CreateZip(("AppxManifest.xml", "<hello/>"));
        using OpcPackage package = OpcPackage.Open(zip);

        using Stream part = package.OpenPart("AppxManifest.xml");
        using var reader = new StreamReader(part);
        Assert.Equal("<hello/>", reader.ReadToEnd());
    }

    [Fact]
    public void OpenPart_MissingPart_ThrowsFileNotFound()
    {
        using MemoryStream zip = CreateZip(("AppxManifest.xml", "<x/>"));
        using OpcPackage package = OpcPackage.Open(zip);

        Assert.Throws<FileNotFoundException>(() => package.OpenPart("DoesNotExist.xml"));
    }

    [Fact]
    public void Open_InvalidData_Throws()
    {
        using var notAZip = new MemoryStream(Encoding.UTF8.GetBytes("this is not a zip"));
        Assert.Throws<InvalidDataException>(() => OpcPackage.Open(notAZip));
    }

    [Fact]
    public void Open_FilePath_RoundTrips()
    {
        string path = Path.Combine(Path.GetTempPath(), $"msixcore-{Guid.NewGuid():N}.msix");
        try
        {
            using (MemoryStream zip = CreateZip(("AppxManifest.xml", "<x/>")))
            {
                File.WriteAllBytes(path, zip.ToArray());
            }

            using OpcPackage package = OpcPackage.Open(path);
            Assert.True(package.ContainsPart("AppxManifest.xml"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_MissingFile_ThrowsFileNotFound()
    {
        string path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.msix");
        Assert.Throws<FileNotFoundException>(() => OpcPackage.Open(path));
    }

    [Fact]
    public void Open_Stream_LeaveOpenFalse_DisposesStream()
    {
        MemoryStream zip = CreateZip(("AppxManifest.xml", "<x/>"));
        OpcPackage package = OpcPackage.Open(zip, leaveOpen: false);
        package.Dispose();

        Assert.Throws<ObjectDisposedException>(() => zip.Position);
    }

    [Fact]
    public void Open_Stream_LeaveOpenTrue_KeepsStreamOpen()
    {
        using MemoryStream zip = CreateZip(("AppxManifest.xml", "<x/>"));
        using (OpcPackage package = OpcPackage.Open(zip, leaveOpen: true))
        {
            Assert.True(package.ContainsPart("AppxManifest.xml"));
        }

        // Should not throw: stream is still usable.
        Assert.Equal(0, zip.Seek(0, SeekOrigin.Begin));
    }
}
