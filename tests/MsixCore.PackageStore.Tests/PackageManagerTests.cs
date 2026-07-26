using MsixCore.Packaging;

namespace MsixCore.PackageStore.Tests;

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

        Directory.Move(dir, Path.Combine(_root, fullName));
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
    public void FindPackage_ByFullName_IsCaseInsensitive()
    {
        (PackageManager manager, string fullName, _) = BuildStoreWithOnePackage();

        using IInstalledPackage? found = manager.FindPackage(fullName.ToUpperInvariant());

        Assert.NotNull(found);
        Assert.Equal(fullName, found!.Identity.PackageFullName);
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
        var store = new FileSystemPackageStore(Path.Combine(_root, "store"));
        var manager = new PackageManager(store);

        IMsixResponse add = manager.AddPackage(msix);

        await Assert.ThrowsAnyAsync<Exception>(() => add.Completion);
        Assert.Equal(InstallationStep.Error, add.Status);
        Assert.NotNull(add.Failure);
        Assert.Empty(store.EnumeratePackages());
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
    public async Task AddPackage_AlreadyInstalled_WithForceApplicationShutdown_StillFails()
    {
        string msix = PackedMsixBuilder.Create(_root, "app.msix");
        var manager = new PackageManager(new FileSystemPackageStore(Path.Combine(_root, "store")));

        await manager.AddPackage(msix).Completion;
        IMsixResponse second = manager.AddPackage(msix, DeploymentOptions.ForceApplicationShutdown);

        await Assert.ThrowsAsync<InvalidOperationException>(() => second.Completion);
    }

    [Fact]
    public async Task AddPackage_AlreadyInstalled_WithForceReinstall_Succeeds()
    {
        string msix = PackedMsixBuilder.Create(_root, "app.msix");
        var manager = new PackageManager(new FileSystemPackageStore(Path.Combine(_root, "store")));

        await manager.AddPackage(msix).Completion;
        IMsixResponse second = manager.AddPackage(msix, DeploymentOptions.ForceReinstall);

        await second.Completion;
        Assert.Equal(InstallationStep.Completed, second.Status);
    }

    [Fact]
    public async Task AddPackage_Upgrade_ReplacesInstalledFamily()
    {
        string version1 = PackedMsixBuilder.Create(
            _root,
            "v1.msix",
            LoosePackageBuilder.ManifestXml(version: "1.0.0.0"));
        string version2 = PackedMsixBuilder.Create(
            _root,
            "v2.msix",
            LoosePackageBuilder.ManifestXml(version: "2.0.0.0"));
        var store = new FileSystemPackageStore(Path.Combine(_root, "store"));
        var manager = new PackageManager(store);

        await manager.AddPackage(version1).Completion;
        await manager.AddPackage(version2).Completion;

        InstalledPackageInfo installed = Assert.Single(store.EnumeratePackages());
        Assert.Equal(new Version(2, 0, 0, 0), installed.Identity.Version);
    }

    [Fact]
    public async Task AddPackage_Downgrade_IsRejectedUnlessAllowed()
    {
        string version2 = PackedMsixBuilder.Create(
            _root,
            "v2.msix",
            LoosePackageBuilder.ManifestXml(version: "2.0.0.0"));
        string version1 = PackedMsixBuilder.Create(
            _root,
            "v1.msix",
            LoosePackageBuilder.ManifestXml(version: "1.0.0.0"));
        var store = new FileSystemPackageStore(Path.Combine(_root, "store"));
        var manager = new PackageManager(store);

        await manager.AddPackage(version2).Completion;
        IMsixResponse rejected = manager.AddPackage(version1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rejected.Completion);
        Assert.Equal(new Version(2, 0, 0, 0), Assert.Single(store.EnumeratePackages()).Identity.Version);

        await manager.AddPackage(version1, DeploymentOptions.AllowDowngrade).Completion;
        Assert.Equal(new Version(1, 0, 0, 0), Assert.Single(store.EnumeratePackages()).Identity.Version);
    }

    [Fact]
    public async Task QueryMetadata_DoesNotOpenContentUntilRequested()
    {
        var parts = new Dictionary<string, byte[]>
        {
            ["Assets/StoreLogo.png"] = [0x89, 0x50, 0x4E, 0x47],
        };
        string msix = PackedMsixBuilder.Create(_root, "app.msix", extraParts: parts);
        var store = new FileSystemPackageStore(Path.Combine(_root, "store"));
        var manager = new PackageManager(store);
        await manager.AddPackage(msix).Completion;

        InstalledPackageInfo info = Assert.Single(store.EnumeratePackages());
        using IInstalledPackage? installed = manager.FindPackage(info.Identity.PackageFullName);

        Assert.NotNull(installed);
        Assert.Equal(info.DisplayName, installed!.DisplayName);
        using Stream? logo = installed.OpenLogo();
        Assert.NotNull(logo);
        Assert.Equal(4, logo!.Length);
        using MsixPackage content = info.OpenPackage();
        Assert.True(content.Opc.ContainsPart("Assets/StoreLogo.png"));
    }

    [Fact]
    public async Task RemovePackage_NotInstalled_Fails()
    {
        var manager = new PackageManager(new FileSystemPackageStore(Path.Combine(_root, "store")));

        IMsixResponse remove = manager.RemovePackage("Nope.NotHere_9.9.9.9_x64__zzzzzzzzzzzzz");

        await Assert.ThrowsAsync<InvalidOperationException>(() => remove.Completion);
    }
}
