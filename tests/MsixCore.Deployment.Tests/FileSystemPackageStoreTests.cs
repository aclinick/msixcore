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

    [Fact]
    public void Commit_BackupDeleteFailure_StillReportsSuccess()
    {
        var store = new FileSystemPackageStore(_root);
        const string fullName = "Contoso.MyApp_1.0.0.0_x64__abcdefgh12345";

        string first = store.CreateStagingLocation();
        File.WriteAllText(Path.Combine(first, "AppxManifest.xml"), LoosePackageBuilder.ManifestXml());
        string readOnly = Path.Combine(first, "readonly.txt");
        File.WriteAllText(readOnly, "v1");
        store.Commit(first, fullName);

        // Mark a file in the installed payload read-only. On the next commit that payload is moved
        // aside to the backup (renaming tolerates read-only files), the new payload is promoted, and
        // then the best-effort backup deletion throws UnauthorizedAccessException on the read-only
        // file. The promotion has already succeeded, so Commit must swallow that and report success.
        string installedReadOnly = Path.Combine(store.GetInstallLocation(fullName), "readonly.txt");
        File.SetAttributes(installedReadOnly, FileAttributes.ReadOnly);
        try
        {
            string second = store.CreateStagingLocation();
            File.WriteAllText(Path.Combine(second, "AppxManifest.xml"), LoosePackageBuilder.ManifestXml());

            store.Commit(second, fullName);

            Assert.True(store.Contains(fullName));
            Assert.False(Directory.Exists(second));
        }
        finally
        {
            // Clear the read-only attribute on the leaked backup so test teardown can delete _root.
            foreach (string file in Directory.EnumerateFiles(_root, "readonly.txt", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
        }
    }

    [Fact]
    public void Commit_ConcurrentCommitsOfSamePackage_LeaveConsistentState()
    {
        var store = new FileSystemPackageStore(_root);
        const string fullName = "Contoso.MyApp_1.0.0.0_x64__abcdefgh12345";

        // Many concurrent commits of the same package must be serialized so no commit's rollback can
        // delete another's promoted install; the end state must be a single valid installation.
        Parallel.For(0, 16, _ =>
        {
            string staging = store.CreateStagingLocation();
            File.WriteAllText(Path.Combine(staging, "AppxManifest.xml"), LoosePackageBuilder.ManifestXml());
            store.Commit(staging, fullName);
        });

        Assert.True(store.Contains(fullName));
        Assert.True(File.Exists(Path.Combine(store.GetInstallLocation(fullName), "AppxManifest.xml")));
    }

    [Fact]
    public void Commit_ConcurrentCommits_DifferentRootCasing_LeaveConsistentState()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // Only Windows resolves differently-cased paths to the same directory.
        }

        // Two stores whose roots differ only by case address the same directory on Windows; the
        // promotion gate must treat them as the same destination so concurrent commits still serialize.
        var lower = new FileSystemPackageStore(_root);
        var upper = new FileSystemPackageStore(_root.ToUpperInvariant());
        const string fullName = "Contoso.MyApp_1.0.0.0_x64__abcdefgh12345";

        Parallel.For(0, 16, i =>
        {
            FileSystemPackageStore store = (i % 2 == 0) ? lower : upper;
            string staging = store.CreateStagingLocation();
            File.WriteAllText(Path.Combine(staging, "AppxManifest.xml"), LoosePackageBuilder.ManifestXml());
            store.Commit(staging, fullName);
        });

        Assert.True(lower.Contains(fullName));
        Assert.True(File.Exists(Path.Combine(lower.GetInstallLocation(fullName), "AppxManifest.xml")));
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
