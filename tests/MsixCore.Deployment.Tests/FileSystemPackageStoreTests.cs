using MsixCore.Packaging;

namespace MsixCore.Deployment.Tests;

public class FileSystemPackageStoreTests : IDisposable
{
    private readonly string _root;

    public FileSystemPackageStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msixcore-store-" + Guid.NewGuid().ToString("N"));
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
    public void EnumeratePackages_MissingRoot_ReturnsEmpty()
    {
        var store = new FileSystemPackageStore(_root);
        Assert.Empty(store.EnumeratePackages());
    }

    [Fact]
    public void EnumeratePackages_ReturnsFoldersWithManifest()
    {
        LoosePackageBuilder.Create(_root, "pkgA");
        LoosePackageBuilder.Create(_root, "pkgB", LoosePackageBuilder.ManifestXml(name: "Contoso.Other"));

        var store = new FileSystemPackageStore(_root);
        IReadOnlyList<IInstalledPackage> packages = store.EnumeratePackages();

        try
        {
            Assert.Equal(2, packages.Count);
        }
        finally
        {
            foreach (IInstalledPackage p in packages)
            {
                p.Dispose();
            }
        }
    }

    [Fact]
    public void EnumeratePackages_IgnoresFoldersWithoutManifest()
    {
        Directory.CreateDirectory(Path.Combine(_root, "not-a-package"));
        LoosePackageBuilder.Create(_root, "pkgA");

        var store = new FileSystemPackageStore(_root);
        IReadOnlyList<IInstalledPackage> packages = store.EnumeratePackages();

        try
        {
            Assert.Single(packages);
        }
        finally
        {
            foreach (IInstalledPackage p in packages)
            {
                p.Dispose();
            }
        }
    }

    [Fact]
    public void RootDirectory_IsAbsolute()
    {
        var store = new FileSystemPackageStore(_root);
        Assert.True(Path.IsPathFullyQualified(store.RootDirectory));
    }

    [Fact]
    public void Constructor_NullOrEmpty_Throws()
    {
        Assert.Throws<ArgumentException>(() => new FileSystemPackageStore(""));
        Assert.Throws<ArgumentNullException>(() => new FileSystemPackageStore(null!));
    }

    [Fact]
    public void Commit_PromotesStagingAndContainsFindsIt()
    {
        var store = new FileSystemPackageStore(_root);
        const string fullName = "Contoso.MyApp_1.0.0.0_x64__abcdefgh12345";

        Assert.False(store.Contains(fullName));

        string staging = store.CreateStagingLocation();
        File.WriteAllText(Path.Combine(staging, "AppxManifest.xml"), LoosePackageBuilder.ManifestXml());

        store.Commit(staging, fullName);

        Assert.True(store.Contains(fullName));
        Assert.False(Directory.Exists(staging));
        Assert.Equal(Path.Combine(_root, fullName), store.GetInstallLocation(fullName));
    }

    [Fact]
    public void Commit_ReplacesExistingPayload()
    {
        var store = new FileSystemPackageStore(_root);
        const string fullName = "Contoso.MyApp_1.0.0.0_x64__abcdefgh12345";

        string first = store.CreateStagingLocation();
        File.WriteAllText(Path.Combine(first, "AppxManifest.xml"), LoosePackageBuilder.ManifestXml());
        File.WriteAllText(Path.Combine(first, "old.txt"), "old");
        store.Commit(first, fullName);

        string second = store.CreateStagingLocation();
        File.WriteAllText(Path.Combine(second, "AppxManifest.xml"), LoosePackageBuilder.ManifestXml());
        store.Commit(second, fullName);

        Assert.True(store.Contains(fullName));
        Assert.False(File.Exists(Path.Combine(store.GetInstallLocation(fullName), "old.txt")));
    }

    [Fact]
    public void Delete_RemovesPayload()
    {
        var store = new FileSystemPackageStore(_root);
        const string fullName = "Contoso.MyApp_1.0.0.0_x64__abcdefgh12345";
        string staging = store.CreateStagingLocation();
        File.WriteAllText(Path.Combine(staging, "AppxManifest.xml"), LoosePackageBuilder.ManifestXml());
        store.Commit(staging, fullName);

        store.Delete(fullName);

        Assert.False(store.Contains(fullName));
    }

    [Fact]
    public void Delete_NotInstalled_IsNoOp()
    {
        var store = new FileSystemPackageStore(_root);
        store.Delete("Nope.NotHere_9.9.9.9_x64__zzzzzzzzzzzzz");
    }

    [Fact]
    public void EnumeratePackages_ExcludesStagingFolder()
    {
        var store = new FileSystemPackageStore(_root);
        // A staging directory with a manifest must never be reported as an installed package.
        string staging = store.CreateStagingLocation();
        File.WriteAllText(Path.Combine(staging, "AppxManifest.xml"), LoosePackageBuilder.ManifestXml());

        Assert.Empty(store.EnumeratePackages());
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("..")]
    public void GetInstallLocation_TraversingFullName_Throws(string fullName)
    {
        var store = new FileSystemPackageStore(_root);
        Assert.Throws<ArgumentException>(() => store.GetInstallLocation(fullName));
    }
}
