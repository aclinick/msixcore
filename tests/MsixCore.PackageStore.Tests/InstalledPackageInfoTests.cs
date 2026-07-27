using MsixCore.Packaging;

namespace MsixCore.PackageStore.Tests;

/// <summary>
/// Covers reading metadata back from an installed (loose) package layout on disk — the input a
/// caller assembles before asking <see cref="DependencyResolver"/> what is satisfied.
/// </summary>
public class InstalledPackageInfoTests : IDisposable
{
    private readonly string _root;

    public InstalledPackageInfoTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msixcore-info-" + Guid.NewGuid().ToString("N"));
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
    public void ReadFromDirectory_ReadsIdentityAndMetadata()
    {
        string directory = LoosePackageBuilder.Create(_root, "app");

        InstalledPackageInfo info = InstalledPackageInfo.ReadFromDirectory(directory);

        Assert.Equal("Contoso.MyApp", info.Identity.Name);
        Assert.Equal("CN=Contoso", info.Identity.Publisher);
        Assert.Equal(new Version(1, 0, 0, 0), info.Identity.Version);
        Assert.Equal(ProcessorArchitecture.X64, info.Identity.Architecture);
        Assert.Equal("Contoso My App", info.DisplayName);
        Assert.Equal("Contoso Ltd", info.PublisherDisplayName);
        Assert.Equal(Path.GetFullPath(directory), info.InstalledLocation);
        Assert.False(info.IsFramework);
    }

    [Fact]
    public void ReadFromDirectory_ReadsTheFrameworkFlag()
    {
        // Resolution treats frameworks differently from ordinary packages, so this flag has to
        // survive the round trip through disk.
        string directory = LoosePackageBuilder.Create(
            _root,
            "framework",
            LoosePackageBuilder.ManifestXml(isFramework: true));

        Assert.True(InstalledPackageInfo.ReadFromDirectory(directory).IsFramework);
    }

    [Fact]
    public void ReadFromDirectory_ReadsTheLogoAndExecutablePaths()
    {
        string directory = LoosePackageBuilder.Create(_root, "app");

        InstalledPackageInfo info = InstalledPackageInfo.ReadFromDirectory(directory);

        Assert.Equal(@"Assets\StoreLogo.png", info.LogoPath);
        Assert.Equal("App/App.exe", info.ExecutablePath);
    }

    [Fact]
    public void ReadFromDirectory_WithNoApplication_HasNoExecutablePath()
    {
        string directory = LoosePackageBuilder.Create(
            _root,
            "framework",
            LoosePackageBuilder.ManifestXml(executable: null, isFramework: true),
            includeExecutable: false);

        Assert.Null(InstalledPackageInfo.ReadFromDirectory(directory).ExecutablePath);
    }

    [Fact]
    public void ReadFromDirectory_NormalisesToAnAbsolutePath()
    {
        string directory = LoosePackageBuilder.Create(_root, "app");
        string relative = Path.Combine(directory, ".", "..", "app");

        Assert.Equal(
            Path.GetFullPath(directory),
            InstalledPackageInfo.ReadFromDirectory(relative).InstalledLocation);
    }

    [Fact]
    public void ReadFromDirectory_WithoutAManifest_Throws()
    {
        string directory = Path.Combine(_root, "empty");
        Directory.CreateDirectory(directory);

        Exception error = Assert.ThrowsAny<Exception>(
            () => InstalledPackageInfo.ReadFromDirectory(directory));

        Assert.Equal(MsixErrorCode.FootprintMissing, MsixError.GetCode(error));
        Assert.Contains("AppxManifest.xml", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadFromDirectory_WhereTheManifestIsADirectory_Throws()
    {
        // A directory named AppxManifest.xml must be rejected rather than read as a file.
        string directory = Path.Combine(_root, "trap");
        Directory.CreateDirectory(Path.Combine(directory, "AppxManifest.xml"));

        Exception error = Assert.ThrowsAny<Exception>(
            () => InstalledPackageInfo.ReadFromDirectory(directory));

        Assert.Equal(MsixErrorCode.PackageStore, MsixError.GetCode(error));
        Assert.Contains("not a regular file", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ReadFromDirectory_WithNoDirectory_Throws(string? directory)
    {
        Assert.ThrowsAny<ArgumentException>(() => InstalledPackageInfo.ReadFromDirectory(directory!));
    }

    [Fact]
    public void OpenPackage_ReadsTheInstalledContent()
    {
        string directory = LoosePackageBuilder.Create(_root, "app");
        InstalledPackageInfo info = InstalledPackageInfo.ReadFromDirectory(directory);

        using MsixPackage package = info.OpenPackage();

        Assert.Equal("Contoso.MyApp", package.Identity.Name);
    }
}
