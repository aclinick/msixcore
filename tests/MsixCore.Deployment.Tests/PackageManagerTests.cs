using MsixCore.Packaging;

namespace MsixCore.Deployment.Tests;

public class PackageManagerTests : IDisposable
{
    private readonly string _root;

    public PackageManagerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msixcore-pm-" + Guid.NewGuid().ToString("N"));
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

    private (PackageManager Manager, string FullName, string FamilyName) BuildStoreWithOnePackage()
    {
        string dir = LoosePackageBuilder.Create(_root, "pkgA");
        string fullName;
        string familyName;
        using (var probe = InstalledPackage.OpenDirectory(dir))
        {
            fullName = probe.Identity.PackageFullName;
            familyName = probe.Identity.PackageFamilyName;
        }

        var manager = new PackageManager(new FileSystemPackageStore(_root));
        return (manager, fullName, familyName);
    }

    [Fact]
    public void FindPackage_ByFullName_ReturnsMatch()
    {
        (PackageManager manager, string fullName, _) = BuildStoreWithOnePackage();

        using IInstalledPackage? found = manager.FindPackage(fullName);

        Assert.NotNull(found);
        Assert.Equal(fullName, found!.Identity.PackageFullName);
    }

    [Fact]
    public void FindPackage_Unknown_ReturnsNull()
    {
        (PackageManager manager, _, _) = BuildStoreWithOnePackage();

        Assert.Null(manager.FindPackage("Fabrikam.Nope_9.9.9.9_x86__zzzzzzzzzzzzz"));
    }

    [Fact]
    public void FindPackageByFamilyName_ReturnsMatch()
    {
        (PackageManager manager, _, string familyName) = BuildStoreWithOnePackage();

        using IInstalledPackage? found = manager.FindPackageByFamilyName(familyName);

        Assert.NotNull(found);
        Assert.Equal(familyName, found!.Identity.PackageFamilyName);
    }

    [Fact]
    public void FindPackages_WildcardMatchesFullName()
    {
        (PackageManager manager, string fullName, _) = BuildStoreWithOnePackage();

        IReadOnlyList<IInstalledPackage> results = manager.FindPackages("Contoso.MyApp_*");

        try
        {
            Assert.Single(results);
            Assert.Equal(fullName, results[0].Identity.PackageFullName);
        }
        finally
        {
            foreach (IInstalledPackage p in results)
            {
                p.Dispose();
            }
        }
    }

    [Fact]
    public void FindPackages_NoMatch_ReturnsEmpty()
    {
        (PackageManager manager, _, _) = BuildStoreWithOnePackage();

        IReadOnlyList<IInstalledPackage> results = manager.FindPackages("Fabrikam.*");

        Assert.Empty(results);
    }

    [Fact]
    public void GetMsixPackageInfo_ReadsFromFileWithoutInstalling()
    {
        string manifest = LoosePackageBuilder.ManifestXml();
        string msixPath = Path.Combine(_root, "sample.msix");
        using (var stream = new FileStream(msixPath, FileMode.Create))
        using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create))
        {
            using var entry = new StreamWriter(archive.CreateEntry("AppxManifest.xml").Open());
            entry.Write(manifest);
        }

        var manager = new PackageManager(new FileSystemPackageStore(_root));
        using IPackage info = manager.GetMsixPackageInfo(msixPath);

        Assert.Equal("Contoso.MyApp", info.Identity.Name);
    }

    [Fact]
    public void QueryMethods_NullOrEmptyArgument_Throw()
    {
        var manager = new PackageManager(new FileSystemPackageStore(_root));

        Assert.Throws<ArgumentException>(() => manager.FindPackage(""));
        Assert.Throws<ArgumentException>(() => manager.FindPackageByFamilyName(""));
        Assert.Throws<ArgumentException>(() => manager.FindPackages(""));
        Assert.Throws<ArgumentException>(() => manager.GetMsixPackageInfo(""));
    }
}
