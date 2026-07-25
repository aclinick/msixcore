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
        IReadOnlyList<InstalledPackageInfo> packages = store.EnumeratePackages();

        Assert.Equal(2, packages.Count);
    }

    [Fact]
    public void ManifestLookup_IsCaseInsensitiveOnCaseSensitiveFileSystems()
    {
        string temporaryDirectory = LoosePackageBuilder.Create(_root, "temporary");
        InstalledPackageInfo info = InstalledPackageInfo.ReadFromDirectory(temporaryDirectory);
        string directory = Path.Combine(_root, info.Identity.PackageFullName);
        Directory.Move(temporaryDirectory, directory);
        string manifest = Path.Combine(directory, "AppxManifest.xml");
        string temporary = Path.Combine(directory, "manifest.tmp");
        string lowerCaseManifest = Path.Combine(directory, "appxmanifest.xml");
        File.Move(manifest, temporary);
        File.Move(temporary, lowerCaseManifest);

        var store = new FileSystemPackageStore(_root);
        Assert.True(store.Contains(info.Identity.PackageFullName));

        IReadOnlyList<InstalledPackageInfo> packages = store.EnumeratePackages();
        Assert.Single(packages);
    }

    [Fact]
    public void EnumeratePackages_IgnoresFoldersWithoutManifest()
    {
        Directory.CreateDirectory(Path.Combine(_root, "not-a-package"));
        LoosePackageBuilder.Create(_root, "pkgA");

        var store = new FileSystemPackageStore(_root);
        IReadOnlyList<InstalledPackageInfo> packages = store.EnumeratePackages();

        Assert.Single(packages);
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
        string staging = store.CreateStagingLocation();
        File.WriteAllText(Path.Combine(staging, "AppxManifest.xml"), LoosePackageBuilder.ManifestXml());
        InstalledPackageInfo info = InstalledPackageInfo.ReadFromDirectory(staging);
        string fullName = info.Identity.PackageFullName;

        Assert.False(store.Contains(fullName));
        store.Commit(staging, info, DeploymentOptions.None);

        Assert.True(store.Contains(fullName));
        Assert.False(Directory.Exists(staging));
        Assert.Equal(Path.Combine(_root, fullName), store.GetInstallLocation(fullName));
    }

    [Fact]
    public void Commit_ReplacesExistingPayload()
    {
        var store = new FileSystemPackageStore(_root);
        string first = store.CreateStagingLocation();
        File.WriteAllText(Path.Combine(first, "AppxManifest.xml"), LoosePackageBuilder.ManifestXml());
        File.WriteAllText(Path.Combine(first, "old.txt"), "old");
        InstalledPackageInfo firstInfo = InstalledPackageInfo.ReadFromDirectory(first);
        string fullName = firstInfo.Identity.PackageFullName;
        store.Commit(first, firstInfo, DeploymentOptions.None);

        string second = store.CreateStagingLocation();
        File.WriteAllText(Path.Combine(second, "AppxManifest.xml"), LoosePackageBuilder.ManifestXml());
        InstalledPackageInfo secondInfo = InstalledPackageInfo.ReadFromDirectory(second);
        store.Commit(second, secondInfo, DeploymentOptions.ForceReinstall);

        Assert.True(store.Contains(fullName));
        Assert.False(File.Exists(Path.Combine(store.GetInstallLocation(fullName), "old.txt")));
    }

    [Fact]
    public void Delete_RemovesPayload()
    {
        var store = new FileSystemPackageStore(_root);
        string staging = store.CreateStagingLocation();
        File.WriteAllText(Path.Combine(staging, "AppxManifest.xml"), LoosePackageBuilder.ManifestXml());
        InstalledPackageInfo info = InstalledPackageInfo.ReadFromDirectory(staging);
        string fullName = info.Identity.PackageFullName;
        store.Commit(staging, info, DeploymentOptions.None);

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

    [WindowsFact]
    public void Commit_BackupDeleteFailure_StillReportsSuccess()
    {
        var store = new FileSystemPackageStore(_root);
        string first = store.CreateStagingLocation();
        File.WriteAllText(Path.Combine(first, "AppxManifest.xml"), LoosePackageBuilder.ManifestXml());
        string readOnly = Path.Combine(first, "readonly.txt");
        File.WriteAllText(readOnly, "v1");
        InstalledPackageInfo firstInfo = InstalledPackageInfo.ReadFromDirectory(first);
        string fullName = firstInfo.Identity.PackageFullName;
        store.Commit(first, firstInfo, DeploymentOptions.None);

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
            InstalledPackageInfo secondInfo = InstalledPackageInfo.ReadFromDirectory(second);

            store.Commit(second, secondInfo, DeploymentOptions.ForceReinstall);

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
        // Many concurrent commits of the same package must be serialized so no commit's rollback can
        // delete another's promoted install; the end state must be a single valid installation.
        Parallel.For(0, 16, _ =>
        {
            string staging = store.CreateStagingLocation();
            File.WriteAllText(Path.Combine(staging, "AppxManifest.xml"), LoosePackageBuilder.ManifestXml());
            InstalledPackageInfo info = InstalledPackageInfo.ReadFromDirectory(staging);
            store.Commit(staging, info, DeploymentOptions.ForceReinstall);
        });

        string fullName = InstalledPackageInfo.ReadFromDirectory(
            Directory.EnumerateDirectories(_root).Single(path => !Path.GetFileName(path).StartsWith('.')))
            .Identity.PackageFullName;
        Assert.True(store.Contains(fullName));
        Assert.True(File.Exists(Path.Combine(store.GetInstallLocation(fullName), "AppxManifest.xml")));
    }

    [WindowsFact]
    public void Commit_ConcurrentCommits_DifferentRootCasing_LeaveConsistentState()
    {
        // Two stores whose roots differ only by case address the same directory on Windows; the
        // promotion gate must treat them as the same destination so concurrent commits still serialize.
        var lower = new FileSystemPackageStore(_root);
        var upper = new FileSystemPackageStore(_root.ToUpperInvariant());
        Parallel.For(0, 16, i =>
        {
            FileSystemPackageStore store = (i % 2 == 0) ? lower : upper;
            string staging = store.CreateStagingLocation();
            File.WriteAllText(Path.Combine(staging, "AppxManifest.xml"), LoosePackageBuilder.ManifestXml());
            InstalledPackageInfo info = InstalledPackageInfo.ReadFromDirectory(staging);
            store.Commit(staging, info, DeploymentOptions.ForceReinstall);
        });

        string fullName = lower.EnumeratePackages().Single().Identity.PackageFullName;
        Assert.True(lower.Contains(fullName));
        Assert.True(File.Exists(Path.Combine(lower.GetInstallLocation(fullName), "AppxManifest.xml")));
    }

    [Fact]
    public async Task Commit_WaitsForCrossProcessLock_ThenPromotes()
    {
        var store = new FileSystemPackageStore(_root);
        string staging = store.CreateStagingLocation();
        File.WriteAllText(Path.Combine(staging, "AppxManifest.xml"), LoosePackageBuilder.ManifestXml());
        InstalledPackageInfo info = InstalledPackageInfo.ReadFromDirectory(staging);
        string lockPath = Path.Combine(_root, FileSystemPackageStore.CommitLockFileName);

        Task commit;
        using (var externalLock = new FileStream(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None))
        {
            commit = Task.Run(() => store.Commit(staging, info, DeploymentOptions.None));
            await Task.Delay(150);
            Assert.False(commit.IsCompleted);
        }

        await commit.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(store.Contains(info.Identity.PackageFullName));
    }

    [Fact]
    public void Commit_Failure_ReleasesCrossProcessLock()
    {
        var store = new FileSystemPackageStore(_root);
        string first = store.CreateStagingLocation();
        File.WriteAllText(Path.Combine(first, "AppxManifest.xml"), LoosePackageBuilder.ManifestXml());
        InstalledPackageInfo firstInfo = InstalledPackageInfo.ReadFromDirectory(first);
        store.Commit(first, firstInfo, DeploymentOptions.None);

        string duplicate = store.CreateStagingLocation();
        File.WriteAllText(Path.Combine(duplicate, "AppxManifest.xml"), LoosePackageBuilder.ManifestXml());
        InstalledPackageInfo duplicateInfo = InstalledPackageInfo.ReadFromDirectory(duplicate);
        Assert.Throws<InvalidOperationException>(
            () => store.Commit(duplicate, duplicateInfo, DeploymentOptions.None));

        using var externalLock = new FileStream(
            Path.Combine(_root, FileSystemPackageStore.CommitLockFileName),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
    }

    [Fact]
    public void Query_AfterCrashBetweenBackupAndPromotion_RecoversPackage()
    {
        var initialStore = new FileSystemPackageStore(_root);
        string version1 = initialStore.CreateStagingLocation();
        File.WriteAllText(
            Path.Combine(version1, "AppxManifest.xml"),
            LoosePackageBuilder.ManifestXml(version: "1.0.0.0"));
        InstalledPackageInfo version1Info = InstalledPackageInfo.ReadFromDirectory(version1);
        initialStore.Commit(version1, version1Info, DeploymentOptions.None);

        string version2 = initialStore.CreateStagingLocation();
        File.WriteAllText(
            Path.Combine(version2, "AppxManifest.xml"),
            LoosePackageBuilder.ManifestXml(version: "2.0.0.0"));
        InstalledPackageInfo version2Info = InstalledPackageInfo.ReadFromDirectory(version2);
        var crashingStore = new FileSystemPackageStore(
            _root,
            Directory.EnumerateDirectories,
            static point => point == CommitFaultPoint.AfterBackupsMovedBeforePromotion);

        Assert.Throws<SimulatedProcessCrashException>(
            () => crashingStore.Commit(version2, version2Info, DeploymentOptions.None));
        Assert.True(File.Exists(Path.Combine(_root, FileSystemPackageStore.CommitJournalFileName)));

        var recoveredStore = new FileSystemPackageStore(_root);
        InstalledPackageInfo? recovered =
            recoveredStore.FindByFamilyName(version1Info.Identity.PackageFamilyName);

        Assert.NotNull(recovered);
        Assert.Equal(new Version(2, 0, 0, 0), recovered!.Identity.Version);
        Assert.Single(recoveredStore.EnumeratePackages());
        Assert.False(File.Exists(Path.Combine(_root, FileSystemPackageStore.CommitJournalFileName)));
    }

    [Fact]
    public void Commit_UnreadableInstalledManifest_FailsClosed()
    {
        var store = new FileSystemPackageStore(_root);
        string version2 = store.CreateStagingLocation();
        File.WriteAllText(
            Path.Combine(version2, "AppxManifest.xml"),
            LoosePackageBuilder.ManifestXml(version: "2.0.0.0"));
        InstalledPackageInfo version2Info = InstalledPackageInfo.ReadFromDirectory(version2);
        store.Commit(version2, version2Info, DeploymentOptions.None);

        string version1 = store.CreateStagingLocation();
        File.WriteAllText(
            Path.Combine(version1, "AppxManifest.xml"),
            LoosePackageBuilder.ManifestXml(version: "1.0.0.0"));
        InstalledPackageInfo version1Info = InstalledPackageInfo.ReadFromDirectory(version1);
        string installedManifest = Path.Combine(
            store.GetInstallLocation(version2Info.Identity.PackageFullName),
            "AppxManifest.xml");

        using (var lockedManifest = new FileStream(
            installedManifest,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None))
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => store.Commit(version1, version1Info, DeploymentOptions.AllowDowngrade));
            Assert.Contains("metadata", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        Assert.True(store.Contains(version2Info.Identity.PackageFullName));
        Assert.False(store.Contains(version1Info.Identity.PackageFullName));
    }

    [Fact]
    public void FindByFullName_DoesNotEnumerateStoreOrPayload()
    {
        string temporary = LoosePackageBuilder.Create(_root, "temporary");
        InstalledPackageInfo temporaryInfo = InstalledPackageInfo.ReadFromDirectory(temporary);
        string directory = Path.Combine(_root, temporaryInfo.Identity.PackageFullName);
        Directory.Move(temporary, directory);
        InstalledPackageInfo expected = InstalledPackageInfo.ReadFromDirectory(directory);
        var store = new FileSystemPackageStore(
            _root,
            _ => throw new InvalidOperationException("Directory enumeration must not occur."));

        InstalledPackageInfo? found = store.FindByFullName(expected.Identity.PackageFullName);

        Assert.NotNull(found);
        Assert.Equal(expected.Identity, found!.Identity);
    }

    [Fact]
    public void FindByFullName_MismatchedDirectoryAndManifest_ReturnsNull()
    {
        string probe = LoosePackageBuilder.Create(_root, "probe");
        InstalledPackageInfo requested = InstalledPackageInfo.ReadFromDirectory(probe);
        Directory.Delete(probe, recursive: true);
        LoosePackageBuilder.Create(
            _root,
            requested.Identity.PackageFullName,
            LoosePackageBuilder.ManifestXml(name: "Fabrikam.Different"));
        var store = new FileSystemPackageStore(_root);

        InstalledPackageInfo? found = store.FindByFullName(requested.Identity.PackageFullName);

        Assert.Null(found);
    }

    [Fact]
    public void FindByFamilyName_LegacySideBySideState_ReturnsNewestDeterministically()
    {
        string version1 = LoosePackageBuilder.Create(
            _root,
            "v1",
            LoosePackageBuilder.ManifestXml(version: "1.0.0.0"));
        string version2 = LoosePackageBuilder.Create(
            _root,
            "v2",
            LoosePackageBuilder.ManifestXml(version: "2.0.0.0"));
        InstalledPackageInfo v1 = InstalledPackageInfo.ReadFromDirectory(version1);
        InstalledPackageInfo v2 = InstalledPackageInfo.ReadFromDirectory(version2);
        Directory.Move(version1, Path.Combine(_root, v1.Identity.PackageFullName));
        Directory.Move(version2, Path.Combine(_root, v2.Identity.PackageFullName));
        var store = new FileSystemPackageStore(_root);

        InstalledPackageInfo? found = store.FindByFamilyName(v1.Identity.PackageFamilyName);

        Assert.NotNull(found);
        Assert.Equal(new Version(2, 0, 0, 0), found!.Identity.Version);
    }

    [Fact]
    public void EnumeratePackages_ReturnsManifestMetadata()
    {
        string directory = LoosePackageBuilder.Create(_root, "pkgA");
        var store = new FileSystemPackageStore(_root);

        InstalledPackageInfo info = Assert.Single(store.EnumeratePackages());

        Assert.Equal("Contoso.MyApp", info.Identity.Name);
        Assert.Equal("Contoso My App", info.DisplayName);
        Assert.Equal(Path.GetFullPath(directory), info.InstalledLocation);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("a?b")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    [InlineData("..")]
    public void GetInstallLocation_InvalidFullName_Throws(string fullName)
    {
        var store = new FileSystemPackageStore(_root);
        Assert.Throws<ArgumentException>(() => store.GetInstallLocation(fullName));
    }
}

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "This test requires Windows case-insensitive path semantics.";
        }
    }
}
