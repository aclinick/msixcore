using MsixCore.Packaging;

namespace MsixCore.Deployment.Tests;

public class InstalledPackageTests : IDisposable
{
    private readonly string _root;

    public InstalledPackageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msixcore-inst-" + Guid.NewGuid().ToString("N"));
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
    public void OpenDirectory_ExposesIdentityAndInstalledLocation()
    {
        string dir = LoosePackageBuilder.Create(_root, "pkgA");

        using var package = InstalledPackage.OpenDirectory(dir);

        Assert.Equal("Contoso.MyApp", package.Identity.Name);
        Assert.Equal("Contoso My App", package.DisplayName);
        Assert.Equal(Path.GetFullPath(dir), package.InstalledLocation);
    }

    [Fact]
    public void ExecutionInfo_ResolvesExecutableUnderInstallLocation()
    {
        string dir = LoosePackageBuilder.Create(_root, "pkgA");

        using var package = InstalledPackage.OpenDirectory(dir);
        ExecutionInfo? info = package.ExecutionInfo;

        Assert.NotNull(info);
        Assert.Equal(
            Path.Combine(Path.GetFullPath(dir), "App", "App.exe"),
            info!.ResolvedExecutableFilePath);
        Assert.Equal(Path.GetFullPath(dir), info.WorkingDirectory);
    }

    [Fact]
    public void ExecutionInfo_NullWhenNoExecutable()
    {
        string dir = LoosePackageBuilder.Create(
            _root,
            "pkgNoExe",
            LoosePackageBuilder.ManifestXml(executable: null),
            includeExecutable: false);

        using var package = InstalledPackage.OpenDirectory(dir);

        Assert.Null(package.ExecutionInfo);
    }

    [Fact]
    public void ExecutionInfo_NullWhenExecutableEscapesInstallLocation()
    {
        string dir = LoosePackageBuilder.Create(
            _root,
            "pkgEscape",
            LoosePackageBuilder.ManifestXml(executable: @"..\..\evil.exe"),
            includeExecutable: false);

        using var package = InstalledPackage.OpenDirectory(dir);

        Assert.Null(package.ExecutionInfo);
    }

    [Fact]
    public void OpenLogo_ReturnsStreamWhenLogoPresent()
    {
        string dir = LoosePackageBuilder.Create(_root, "pkgLogo");
        string assets = Path.Combine(dir, "Assets");
        Directory.CreateDirectory(assets);
        File.WriteAllText(Path.Combine(assets, "StoreLogo.png"), "png");

        using var package = InstalledPackage.OpenDirectory(dir);
        using Stream? logo = package.OpenLogo();

        Assert.NotNull(logo);
    }
}
