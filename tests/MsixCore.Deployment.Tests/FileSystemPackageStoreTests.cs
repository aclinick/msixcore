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
}
