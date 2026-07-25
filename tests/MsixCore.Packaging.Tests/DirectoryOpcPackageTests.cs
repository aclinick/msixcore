using System.Text;
using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging.Tests;

public class DirectoryOpcPackageTests : IDisposable
{
    private readonly string _root;

    public DirectoryOpcPackageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msixcore-loose-" + Guid.NewGuid().ToString("N"));
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

    private void WriteFile(string relative, string content)
    {
        string full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, Encoding.UTF8);
    }

    [Fact]
    public void Open_EnumeratesParts_WithForwardSlashNames()
    {
        WriteFile("AppxManifest.xml", "<Package/>");
        WriteFile("Assets/StoreLogo.png", "png");

        using var package = DirectoryOpcPackage.Open(_root);

        Assert.Contains("AppxManifest.xml", package.PartNames);
        Assert.Contains("Assets/StoreLogo.png", package.PartNames);
    }

    [Fact]
    public void ContainsPart_IsCaseInsensitive_AndBackslashTolerant()
    {
        WriteFile("Assets/StoreLogo.png", "png");

        using var package = DirectoryOpcPackage.Open(_root);

        Assert.True(package.ContainsPart("assets/storelogo.png"));
        Assert.True(package.ContainsPart("Assets\\StoreLogo.png"));
        Assert.False(package.ContainsPart("Assets/Missing.png"));
    }

    [Fact]
    public void OpenPart_ReturnsFileContent()
    {
        WriteFile("AppxManifest.xml", "<Package/>");

        using var package = DirectoryOpcPackage.Open(_root);
        using Stream stream = package.OpenPart("AppxManifest.xml");
        using var reader = new StreamReader(stream);

        Assert.Equal("<Package/>", reader.ReadToEnd());
    }

    [Fact]
    public void OpenPart_MissingPart_Throws()
    {
        WriteFile("AppxManifest.xml", "<Package/>");
        using var package = DirectoryOpcPackage.Open(_root);

        Assert.Throws<FileNotFoundException>(() => package.OpenPart("Missing.xml"));
    }

    [Fact]
    public void RootDirectory_IsAbsolute()
    {
        WriteFile("AppxManifest.xml", "<Package/>");
        using var package = DirectoryOpcPackage.Open(_root);

        Assert.True(Path.IsPathFullyQualified(package.RootDirectory));
    }

    [Fact]
    public void Open_MissingDirectory_Throws()
    {
        string missing = Path.Combine(_root, "does-not-exist");
        Assert.Throws<DirectoryNotFoundException>(() => DirectoryOpcPackage.Open(missing));
    }

    [Fact]
    public void Open_NullOrEmpty_Throws()
    {
        Assert.Throws<ArgumentException>(() => DirectoryOpcPackage.Open(""));
        Assert.Throws<ArgumentNullException>(() => DirectoryOpcPackage.Open(null!));
    }
}
