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

    [Fact]
    public async Task AddPackage_InstallsAndBecomesQueryable_ThenRemove()
    {
        string msix = PackedMsixBuilder.Create(_root, "app.msix");
        string expectedFullName;
        using (MsixPackage probe = MsixPackage.Open(msix))
        {
            expectedFullName = probe.Identity.PackageFullName;
        }

        var store = new FileSystemPackageStore(Path.Combine(_root, "store"));
        var manager = new PackageManager(store);

        IMsixResponse add = manager.AddPackage(msix);
        await add.Completion;

        Assert.Equal(InstallationStep.Completed, add.Status);
        Assert.True(store.Contains(expectedFullName));
        using (IInstalledPackage? found = manager.FindPackage(expectedFullName))
        {
            Assert.NotNull(found);
            Assert.Equal(expectedFullName, found!.Identity.PackageFullName);
        }

        IMsixResponse remove = manager.RemovePackage(expectedFullName);
        await remove.Completion;

        Assert.Equal(InstallationStep.Completed, remove.Status);
        Assert.False(store.Contains(expectedFullName));
        Assert.Null(manager.FindPackage(expectedFullName));
    }

    [Fact]
    public async Task AddPackage_CorruptBlockMap_Fails()
    {
        string msix = PackedMsixBuilder.Create(_root, "bad.msix", validBlockMap: false);
        var manager = new PackageManager(new FileSystemPackageStore(Path.Combine(_root, "store")));

        IMsixResponse add = manager.AddPackage(msix);

        await Assert.ThrowsAnyAsync<Exception>(() => add.Completion);
        Assert.Equal(InstallationStep.Error, add.Status);
        Assert.NotNull(add.Failure);
    }

    [Fact]
    public async Task AddPackage_AlreadyInstalled_Fails()
    {
        string msix = PackedMsixBuilder.Create(_root, "app.msix");
        var manager = new PackageManager(new FileSystemPackageStore(Path.Combine(_root, "store")));

        await manager.AddPackage(msix).Completion;
        IMsixResponse second = manager.AddPackage(msix);

        await Assert.ThrowsAsync<InvalidOperationException>(() => second.Completion);
    }

    [Fact]
    public async Task AddPackage_AlreadyInstalled_WithForce_Succeeds()
    {
        string msix = PackedMsixBuilder.Create(_root, "app.msix");
        var manager = new PackageManager(new FileSystemPackageStore(Path.Combine(_root, "store")));

        await manager.AddPackage(msix).Completion;
        IMsixResponse second = manager.AddPackage(msix, DeploymentOptions.ForceApplicationShutdown);

        await second.Completion;
        Assert.Equal(InstallationStep.Completed, second.Status);
    }

    [Fact]
    public async Task RemovePackage_NotInstalled_Fails()
    {
        var manager = new PackageManager(new FileSystemPackageStore(Path.Combine(_root, "store")));

        IMsixResponse remove = manager.RemovePackage("Nope.NotHere_9.9.9.9_x64__zzzzzzzzzzzzz");

        await Assert.ThrowsAsync<InvalidOperationException>(() => remove.Completion);
    }
}
