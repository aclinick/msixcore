using MsixCore.Packaging;

namespace MsixCore.PackageStore.Tests;

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
    public void Commit_BackupDeleteFailure_RetainsJournalUntilRecovery()
    {
        var store = new FileSystemPackageStore(_root);
        string first = store.CreateStagingLocation();
        File.WriteAllText(Path.Combine(first, "AppxManifest.xml"), LoosePackageBuilder.ManifestXml());
        string readOnly = Path.Combine(first, "readonly.txt");
        File.WriteAllText(readOnly, "v1");
        InstalledPackageInfo firstInfo = InstalledPackageInfo.ReadFromDirectory(first);
        string fullName = firstInfo.Identity.PackageFullName;
        store.Commit(first, firstInfo, DeploymentOptions.None);

        // Renaming tolerates the read-only payload, but backup deletion cannot complete until the
        // attribute is cleared. The journal must remain so a later operation retries cleanup.
        string installedReadOnly = Path.Combine(store.GetInstallLocation(fullName), "readonly.txt");
        File.SetAttributes(installedReadOnly, FileAttributes.ReadOnly);
        try
        {
            string second = store.CreateStagingLocation();
            File.WriteAllText(Path.Combine(second, "AppxManifest.xml"), LoosePackageBuilder.ManifestXml());
            InstalledPackageInfo secondInfo = InstalledPackageInfo.ReadFromDirectory(second);

            Assert.Throws<AggregateException>(
                () => store.Commit(second, secondInfo, DeploymentOptions.ForceReinstall));

            Assert.False(Directory.Exists(second));
            Assert.True(Directory.Exists(store.GetInstallLocation(fullName)));
            Assert.True(File.Exists(Path.Combine(_root, FileSystemPackageStore.CommitJournalFileName)));
            Assert.Contains(
                Directory.EnumerateDirectories(_root),
                directory => Path.GetFileName(directory).Contains(".bak-", StringComparison.Ordinal));
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(_root, "readonly.txt", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
        }

        var recoveredStore = new FileSystemPackageStore(_root);
        Assert.Single(recoveredStore.EnumeratePackages());
        AssertNoTransactionArtifacts();
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

    [Theory]
    [InlineData((int)CommitFaultPoint.MidStagedFileFlush)]
    [InlineData((int)CommitFaultPoint.AfterStagedFilesBeforeDirectoryFlush)]
    [InlineData((int)CommitFaultPoint.AfterStagingDurableBeforeJournal)]
    [InlineData((int)CommitFaultPoint.BeforeJournalDurable)]
    [InlineData((int)CommitFaultPoint.AfterJournalDurableBeforeBackups)]
    [InlineData((int)CommitFaultPoint.MidBackup)]
    [InlineData((int)CommitFaultPoint.AfterBackupsDurableBeforePromotion)]
    [InlineData((int)CommitFaultPoint.AfterPromotionBeforeDurable)]
    [InlineData((int)CommitFaultPoint.AfterPromotionDurableBeforeJournalClear)]
    [InlineData((int)CommitFaultPoint.AfterBackupCleanupBeforeDurable)]
    public void Query_AfterPowerLossAtCommitOrderingPoint_RecoversPackage(int faultPointValue)
    {
        var faultPoint = (CommitFaultPoint)faultPointValue;
        InstalledPackageInfo version1Info = CreateInstalledLayout("1.0.0.0");
        _ = CreateInstalledLayout("2.0.0.0");
        var fileSystem = new RecordingDurableFileSystem(_root, faultPoint);
        var crashingStore = new FileSystemPackageStore(
            _root,
            Directory.EnumerateDirectories,
            fileSystem);
        string version3 = crashingStore.CreateStagingLocation();
        File.WriteAllText(
            Path.Combine(version3, "AppxManifest.xml"),
            LoosePackageBuilder.ManifestXml(version: "3.0.0.0"));
        InstalledPackageInfo version3Info = InstalledPackageInfo.ReadFromDirectory(version3);

        Assert.Throws<SimulatedProcessCrashException>(
            () => crashingStore.Commit(version3, version3Info, DeploymentOptions.None));
        if (faultPoint <= CommitFaultPoint.AfterBackupsDurableBeforePromotion)
        {
            Assert.True(Directory.Exists(version3), $"Staging unexpectedly missing after {faultPoint}.");
        }

        var recoveredStore = new FileSystemPackageStore(_root);
        InstalledPackageInfo? recovered =
            recoveredStore.FindByFamilyName(version1Info.Identity.PackageFamilyName);

        Assert.NotNull(recovered);
        bool journalWasDurable = faultPoint is not (
            CommitFaultPoint.MidStagedFileFlush
            or CommitFaultPoint.AfterStagedFilesBeforeDirectoryFlush
            or CommitFaultPoint.AfterStagingDurableBeforeJournal
            or CommitFaultPoint.BeforeJournalDurable);
        Version expectedVersion = journalWasDurable
            ? new Version(3, 0, 0, 0)
            : new Version(2, 0, 0, 0);
        Assert.Equal(expectedVersion, recovered!.Identity.Version);
        Assert.NotEmpty(recoveredStore.EnumeratePackages());
        Assert.False(File.Exists(Path.Combine(_root, FileSystemPackageStore.CommitJournalFileName)));
    }

    [Fact]
    public void Commit_DurabilityBarriers_PrecedeDestructivePhases()
    {
        _ = CreateInstalledLayout("1.0.0.0");
        var fileSystem = new RecordingDurableFileSystem(_root);
        var store = new FileSystemPackageStore(_root, Directory.EnumerateDirectories, fileSystem);
        string staging = store.CreateStagingLocation();
        File.WriteAllText(
            Path.Combine(staging, "AppxManifest.xml"),
            LoosePackageBuilder.ManifestXml(version: "2.0.0.0"));
        string payloadDirectory = Path.Combine(staging, "Payload");
        Directory.CreateDirectory(payloadDirectory);
        string payload = Path.Combine(payloadDirectory, "data.bin");
        File.WriteAllBytes(payload, [1, 2, 3, 4]);
        InstalledPackageInfo info = InstalledPackageInfo.ReadFromDirectory(staging);

        store.Commit(staging, info, DeploymentOptions.None);

        int manifestFlush = fileSystem.IndexOf(
            "flush-file:" + Path.GetRelativePath(_root, Path.Combine(staging, "AppxManifest.xml")));
        int payloadFlush = fileSystem.IndexOf(
            "flush-file:" + Path.GetRelativePath(_root, payload));
        int payloadDirectoryFlush = fileSystem.IndexOf(
            "flush-directory:" + Path.GetRelativePath(_root, payloadDirectory));
        int stagingDirectoryFlush = fileSystem.IndexOf(
            "flush-directory:" + Path.GetRelativePath(_root, staging));
        int stagingDurable = fileSystem.IndexOf(CommitFaultPoint.AfterStagingDurableBeforeJournal);
        int journalMove = fileSystem.IndexOf("move-journal");
        int beforeJournalDurable = fileSystem.IndexOf(CommitFaultPoint.BeforeJournalDurable);
        int journalFlush = fileSystem.IndexOf("flush-root", beforeJournalDurable + 1);
        int afterJournalDurable = fileSystem.IndexOf(CommitFaultPoint.AfterJournalDurableBeforeBackups);
        int backupMove = fileSystem.IndexOf("move-backup");
        int backupFlush = fileSystem.IndexOf("flush-root", backupMove + 1);
        int afterBackupsDurable = fileSystem.IndexOf(CommitFaultPoint.AfterBackupsDurableBeforePromotion);
        int promotionMove = fileSystem.IndexOf("move-promotion");
        int afterPromotionMove = fileSystem.IndexOf(CommitFaultPoint.AfterPromotionBeforeDurable);
        int promotionFlush = fileSystem.IndexOf("flush-root", afterPromotionMove + 1);
        int afterPromotionDurable =
            fileSystem.IndexOf(CommitFaultPoint.AfterPromotionDurableBeforeJournalClear);
        int backupDelete = fileSystem.IndexOf("delete-backup");
        int afterBackupCleanup =
            fileSystem.IndexOf(CommitFaultPoint.AfterBackupCleanupBeforeDurable);
        int backupCleanupFlush = fileSystem.IndexOf("flush-root", afterBackupCleanup + 1);
        int journalDelete = fileSystem.IndexOf("delete-journal");
        int journalDeleteFlush = fileSystem.IndexOf("flush-root", journalDelete + 1);

        Assert.True(manifestFlush < payloadDirectoryFlush);
        Assert.True(payloadFlush < payloadDirectoryFlush);
        Assert.True(payloadDirectoryFlush < stagingDirectoryFlush);
        Assert.True(stagingDirectoryFlush < stagingDurable);
        Assert.True(stagingDurable < journalMove);
        Assert.True(journalMove < beforeJournalDurable);
        Assert.True(beforeJournalDurable < journalFlush);
        Assert.True(journalFlush < afterJournalDurable);
        Assert.True(afterJournalDurable < backupMove);
        Assert.True(backupMove < backupFlush);
        Assert.True(backupFlush < afterBackupsDurable);
        Assert.True(afterBackupsDurable < promotionMove);
        Assert.True(promotionMove < afterPromotionMove);
        Assert.True(afterPromotionMove < promotionFlush);
        Assert.True(promotionFlush < afterPromotionDurable);
        Assert.True(afterPromotionDurable < backupDelete);
        Assert.True(backupDelete < afterBackupCleanup);
        Assert.True(afterBackupCleanup < backupCleanupFlush);
        Assert.True(backupCleanupFlush < journalDelete);
        Assert.True(journalDelete < journalDeleteFlush);
        Assert.True(fileSystem.PromotionObservedAllFilesFlushed);
    }

    [Fact]
    public void Commit_PromotionSyncFailure_DurablyRollsForward()
    {
        InstalledPackageInfo version1 = CreateInstalledLayout("1.0.0.0");
        var fileSystem = new RecordingDurableFileSystem(_root, promotionFlushFailures: 1);
        var store = new FileSystemPackageStore(_root, Directory.EnumerateDirectories, fileSystem);
        string version2 = store.CreateStagingLocation();
        File.WriteAllText(
            Path.Combine(version2, "AppxManifest.xml"),
            LoosePackageBuilder.ManifestXml(version: "2.0.0.0"));
        InstalledPackageInfo version2Info = InstalledPackageInfo.ReadFromDirectory(version2);

        store.Commit(version2, version2Info, DeploymentOptions.None);

        InstalledPackageInfo installed = Assert.Single(store.EnumeratePackages());
        Assert.Equal(new Version(2, 0, 0, 0), installed.Identity.Version);
        Assert.Equal(version1.Identity.PackageFamilyName, installed.Identity.PackageFamilyName);
        AssertNoTransactionArtifacts();
    }

    [Fact]
    public void Commit_SameVersionPromotionSyncFailure_LeavesNoBackup()
    {
        InstalledPackageInfo original = CreateInstalledLayout("1.0.0.0");
        File.WriteAllText(Path.Combine(original.InstalledLocation, "old.txt"), "old");
        var fileSystem = new RecordingDurableFileSystem(_root, promotionFlushFailures: 1);
        var store = new FileSystemPackageStore(_root, Directory.EnumerateDirectories, fileSystem);
        string replacement = store.CreateStagingLocation();
        File.WriteAllText(
            Path.Combine(replacement, "AppxManifest.xml"),
            LoosePackageBuilder.ManifestXml(version: "1.0.0.0"));
        File.WriteAllText(Path.Combine(replacement, "new.txt"), "new");
        InstalledPackageInfo replacementInfo = InstalledPackageInfo.ReadFromDirectory(replacement);

        store.Commit(replacement, replacementInfo, DeploymentOptions.ForceReinstall);

        InstalledPackageInfo installed = Assert.Single(store.EnumeratePackages());
        Assert.True(File.Exists(Path.Combine(installed.InstalledLocation, "new.txt")));
        Assert.False(File.Exists(Path.Combine(installed.InstalledLocation, "old.txt")));
        AssertNoTransactionArtifacts();
    }

    [Fact]
    public void Commit_PromotionSyncFailureThatRollsBack_ReportsFailure()
    {
        InstalledPackageInfo version1 = CreateInstalledLayout("1.0.0.0");
        var fileSystem = new RecordingDurableFileSystem(
            _root,
            promotionFlushFailures: 1,
            loseDestinationOnPromotionFlushFailure: true);
        var store = new FileSystemPackageStore(_root, Directory.EnumerateDirectories, fileSystem);
        string version2 = store.CreateStagingLocation();
        File.WriteAllText(
            Path.Combine(version2, "AppxManifest.xml"),
            LoosePackageBuilder.ManifestXml(version: "2.0.0.0"));
        InstalledPackageInfo version2Info = InstalledPackageInfo.ReadFromDirectory(version2);

        Assert.Throws<IOException>(
            () => store.Commit(version2, version2Info, DeploymentOptions.None));

        InstalledPackageInfo installed = Assert.Single(store.EnumeratePackages());
        Assert.Equal(version1.Identity.PackageFullName, installed.Identity.PackageFullName);
        AssertNoTransactionArtifacts();
    }

    [Fact]
    public void Query_CrashBeforeBackupCleanupIsDurable_RemovesStrandedBackup()
    {
        _ = CreateInstalledLayout("1.0.0.0");
        var fileSystem = new RecordingDurableFileSystem(
            _root,
            CommitFaultPoint.AfterBackupCleanupBeforeDurable);
        var crashingStore = new FileSystemPackageStore(
            _root,
            Directory.EnumerateDirectories,
            fileSystem);
        string version2 = crashingStore.CreateStagingLocation();
        File.WriteAllText(
            Path.Combine(version2, "AppxManifest.xml"),
            LoosePackageBuilder.ManifestXml(version: "2.0.0.0"));
        InstalledPackageInfo version2Info = InstalledPackageInfo.ReadFromDirectory(version2);

        Assert.Throws<SimulatedProcessCrashException>(
            () => crashingStore.Commit(version2, version2Info, DeploymentOptions.None));
        Assert.True(File.Exists(Path.Combine(_root, FileSystemPackageStore.CommitJournalFileName)));
        Assert.Contains(
            Directory.EnumerateDirectories(_root),
            directory => Path.GetFileName(directory).Contains(".bak-", StringComparison.Ordinal));

        var recoveredStore = new FileSystemPackageStore(_root);
        InstalledPackageInfo installed = Assert.Single(recoveredStore.EnumeratePackages());
        Assert.Equal(new Version(2, 0, 0, 0), installed.Identity.Version);
        AssertNoTransactionArtifacts();
    }

    [Fact]
    public void Commit_BackupDeletionFailure_RetainsJournalForRetry()
    {
        _ = CreateInstalledLayout("1.0.0.0");
        var fileSystem = new RecordingDurableFileSystem(_root, backupDeleteFailures: 2);
        var store = new FileSystemPackageStore(_root, Directory.EnumerateDirectories, fileSystem);
        string version2 = store.CreateStagingLocation();
        File.WriteAllText(
            Path.Combine(version2, "AppxManifest.xml"),
            LoosePackageBuilder.ManifestXml(version: "2.0.0.0"));
        InstalledPackageInfo version2Info = InstalledPackageInfo.ReadFromDirectory(version2);

        Assert.Throws<AggregateException>(
            () => store.Commit(version2, version2Info, DeploymentOptions.None));
        Assert.True(File.Exists(Path.Combine(_root, FileSystemPackageStore.CommitJournalFileName)));
        Assert.Contains(
            Directory.EnumerateDirectories(_root),
            directory => Path.GetFileName(directory).Contains(".bak-", StringComparison.Ordinal));

        var recoveredStore = new FileSystemPackageStore(_root);
        InstalledPackageInfo installed = Assert.Single(recoveredStore.EnumeratePackages());
        Assert.Equal(new Version(2, 0, 0, 0), installed.Identity.Version);
        AssertNoTransactionArtifacts();
    }

    [Fact]
    public void Commit_InterruptedFailureConvergence_RetriesFromJournal()
    {
        InstalledPackageInfo version1 = CreateInstalledLayout("1.0.0.0");
        var fileSystem = new RecordingDurableFileSystem(_root, promotionFlushFailures: 2);
        var store = new FileSystemPackageStore(_root, Directory.EnumerateDirectories, fileSystem);
        string version2 = store.CreateStagingLocation();
        File.WriteAllText(
            Path.Combine(version2, "AppxManifest.xml"),
            LoosePackageBuilder.ManifestXml(version: "2.0.0.0"));
        InstalledPackageInfo version2Info = InstalledPackageInfo.ReadFromDirectory(version2);

        Assert.Throws<AggregateException>(
            () => store.Commit(version2, version2Info, DeploymentOptions.None));
        Assert.True(File.Exists(Path.Combine(_root, FileSystemPackageStore.CommitJournalFileName)));

        var recoveredStore = new FileSystemPackageStore(_root);
        InstalledPackageInfo installed = Assert.Single(recoveredStore.EnumeratePackages());
        Assert.Equal(new Version(2, 0, 0, 0), installed.Identity.Version);
        Assert.Equal(version1.Identity.PackageFamilyName, installed.Identity.PackageFamilyName);
        AssertNoTransactionArtifacts();
    }

    [Fact]
    public void Query_WhenPromotionMetadataIsLost_DurablyRestoresOldPackage()
    {
        InstalledPackageInfo version1 = CreateInstalledLayout("1.0.0.0");
        var crashingFileSystem = new RecordingDurableFileSystem(
            _root,
            CommitFaultPoint.AfterPromotionBeforeDurable,
            promotionCrashState: PromotionCrashState.DestinationLost);
        var crashingStore = new FileSystemPackageStore(
            _root,
            Directory.EnumerateDirectories,
            crashingFileSystem);
        string version2 = crashingStore.CreateStagingLocation();
        File.WriteAllText(
            Path.Combine(version2, "AppxManifest.xml"),
            LoosePackageBuilder.ManifestXml(version: "2.0.0.0"));
        InstalledPackageInfo version2Info = InstalledPackageInfo.ReadFromDirectory(version2);

        Assert.Throws<SimulatedProcessCrashException>(
            () => crashingStore.Commit(version2, version2Info, DeploymentOptions.None));

        var recoveredStore = new FileSystemPackageStore(_root);
        InstalledPackageInfo installed = Assert.Single(recoveredStore.EnumeratePackages());
        Assert.Equal(version1.Identity.PackageFullName, installed.Identity.PackageFullName);
        AssertNoTransactionArtifacts();
    }

    [Fact]
    public void Query_WhenPromotionLeavesBothLocations_DurablyChoosesRollbackBeforeMutation()
    {
        InstalledPackageInfo version1 = CreateInstalledLayout("1.0.0.0");
        var crashingFileSystem = new RecordingDurableFileSystem(
            _root,
            CommitFaultPoint.AfterPromotionBeforeDurable,
            promotionCrashState: PromotionCrashState.DestinationAndStaging);
        var crashingStore = new FileSystemPackageStore(
            _root,
            Directory.EnumerateDirectories,
            crashingFileSystem);
        string version2 = crashingStore.CreateStagingLocation();
        File.WriteAllText(
            Path.Combine(version2, "AppxManifest.xml"),
            LoosePackageBuilder.ManifestXml(version: "2.0.0.0"));
        InstalledPackageInfo version2Info = InstalledPackageInfo.ReadFromDirectory(version2);

        Assert.Throws<SimulatedProcessCrashException>(
            () => crashingStore.Commit(version2, version2Info, DeploymentOptions.None));

        var recoveryFileSystem = new RecordingDurableFileSystem(_root);
        var recoveredStore = new FileSystemPackageStore(
            _root,
            Directory.EnumerateDirectories,
            recoveryFileSystem);
        InstalledPackageInfo installed = Assert.Single(recoveredStore.EnumeratePackages());

        int journalReplace = recoveryFileSystem.IndexOf("replace-journal");
        int rollbackModeDurable = recoveryFileSystem.IndexOf("flush-root", journalReplace + 1);
        int firstRollbackMutation = recoveryFileSystem.IndexOf("delete-directory");
        Assert.True(journalReplace < rollbackModeDurable);
        Assert.True(rollbackModeDurable < firstRollbackMutation);
        Assert.Equal(version1.Identity.PackageFullName, installed.Identity.PackageFullName);
        AssertNoTransactionArtifacts();
    }

    [Fact]
    public void Query_InterruptedRollback_RerunsToSameOldPackage()
    {
        InstalledPackageInfo version1 = CreateInstalledLayout("1.0.0.0");
        var crashingFileSystem = new RecordingDurableFileSystem(
            _root,
            CommitFaultPoint.AfterPromotionBeforeDurable,
            promotionCrashState: PromotionCrashState.DestinationLost);
        var crashingStore = new FileSystemPackageStore(
            _root,
            Directory.EnumerateDirectories,
            crashingFileSystem);
        string version2 = crashingStore.CreateStagingLocation();
        File.WriteAllText(
            Path.Combine(version2, "AppxManifest.xml"),
            LoosePackageBuilder.ManifestXml(version: "2.0.0.0"));
        InstalledPackageInfo version2Info = InstalledPackageInfo.ReadFromDirectory(version2);
        Assert.Throws<SimulatedProcessCrashException>(
            () => crashingStore.Commit(version2, version2Info, DeploymentOptions.None));

        var interruptedFileSystem = new RecordingDurableFileSystem(_root, restoreFlushFailures: 1);
        var interruptedStore = new FileSystemPackageStore(
            _root,
            Directory.EnumerateDirectories,
            interruptedFileSystem);
        Assert.Throws<IOException>(() => interruptedStore.EnumeratePackages());
        Assert.True(File.Exists(Path.Combine(_root, FileSystemPackageStore.CommitJournalFileName)));
        int journalReplace = interruptedFileSystem.IndexOf("replace-journal");
        int rollbackModeDurable = interruptedFileSystem.IndexOf("flush-root", journalReplace + 1);
        int restore = interruptedFileSystem.IndexOf("move-restore");
        Assert.True(journalReplace < rollbackModeDurable);
        Assert.True(rollbackModeDurable < restore);

        var recoveredStore = new FileSystemPackageStore(_root);
        InstalledPackageInfo installed = Assert.Single(recoveredStore.EnumeratePackages());
        Assert.Equal(version1.Identity.PackageFullName, installed.Identity.PackageFullName);
        AssertNoTransactionArtifacts();
    }

    [Theory]
    [InlineData("""{"FormatVersion":1,"Payload":"truncated""")]
    [InlineData("""{"FormatVersion":1,"Payload":"e30=","Sha256":"00"}""")]
    public void Query_TornOrCorruptCommitJournal_FailsClosed(string journal)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, FileSystemPackageStore.CommitJournalFileName),
            journal);
        var store = new FileSystemPackageStore(_root);

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => store.EnumeratePackages());

        Assert.Contains("journal", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CorruptCommitJournal_HasPackageStoreCode()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, FileSystemPackageStore.CommitJournalFileName),
            """{"FormatVersion":1,"Payload":"e30=","Sha256":"00"}""");
        var store = new FileSystemPackageStore(_root);

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => store.EnumeratePackages());
        // The category must survive on the exception the caller actually receives. Asserting only on
        // InnerException would pass even if the outer, public-facing exception carried nothing.
        Assert.Equal(MsixErrorCode.PackageStore, MsixError.GetCode(exception));
        InvalidDataException inner = Assert.IsType<InvalidDataException>(exception.InnerException);
        Assert.Equal(MsixErrorCode.PackageStore, MsixError.GetCode(inner));
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

    private InstalledPackageInfo CreateInstalledLayout(string version)
    {
        string temporary = LoosePackageBuilder.Create(
            _root,
            "temporary-" + Guid.NewGuid().ToString("N"),
            LoosePackageBuilder.ManifestXml(version: version));
        InstalledPackageInfo info = InstalledPackageInfo.ReadFromDirectory(temporary);
        string destination = Path.Combine(_root, info.Identity.PackageFullName);
        Directory.Move(temporary, destination);
        return InstalledPackageInfo.ReadFromDirectory(destination);
    }

    private void AssertNoTransactionArtifacts()
    {
        Assert.False(File.Exists(Path.Combine(_root, FileSystemPackageStore.CommitJournalFileName)));
        Assert.DoesNotContain(
            Directory.EnumerateDirectories(_root),
            directory => Path.GetFileName(directory).Contains(".bak-", StringComparison.Ordinal));
    }

    private sealed class RecordingDurableFileSystem(
        string root,
        CommitFaultPoint? faultPoint = null,
        int promotionFlushFailures = 0,
        PromotionCrashState promotionCrashState = PromotionCrashState.Persisted,
        int restoreFlushFailures = 0,
        bool loseDestinationOnPromotionFlushFailure = false,
        int backupDeleteFailures = 0) : IDurableFileSystem
    {
        private readonly DurableFileSystem _inner = DurableFileSystem.Instance;
        private readonly List<string> _events = [];
        private readonly HashSet<string> _flushedFiles =
            new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        private int _remainingPromotionFlushFailures = promotionFlushFailures;
        private int _remainingRestoreFlushFailures = restoreFlushFailures;
        private int _remainingBackupDeleteFailures = backupDeleteFailures;
        private bool _promotionOccurred;
        private bool _restoreOccurred;
        private string? _lastDeletedBackup;
        private string? _promotionSource;
        private string? _promotionDestination;

        public bool PromotionObservedAllFilesFlushed { get; private set; }

        public bool FileExists(string path) => _inner.FileExists(path);

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public IEnumerable<string> EnumerateFileSystemEntries(string path) =>
            _inner.EnumerateFileSystemEntries(path);

        public FileAttributes GetAttributes(string path) => _inner.GetAttributes(path);

        public void FlushFile(string path)
        {
            _events.Add("flush-file:" + Path.GetRelativePath(root, path));
            _inner.FlushFile(path);
            _flushedFiles.Add(Path.GetFullPath(path));
        }

        public void MoveFile(string source, string destination)
        {
            _events.Add(Path.GetFileName(destination) == FileSystemPackageStore.CommitJournalFileName
                ? "move-journal"
                : "move-file");
            _inner.MoveFile(source, destination);
        }

        public void ReplaceFile(string source, string destination)
        {
            _events.Add("replace-journal");
            _inner.ReplaceFile(source, destination);
        }

        public void MoveDirectory(string source, string destination)
        {
            string destinationName = Path.GetFileName(destination);
            bool backup = destinationName.Contains(".bak-", StringComparison.Ordinal);
            bool restore = Path.GetFileName(source).Contains(".bak-", StringComparison.Ordinal);
            _events.Add(backup ? "move-backup" : restore ? "move-restore" : "move-promotion");
            if (restore)
            {
                _restoreOccurred = true;
            }
            else if (!backup)
            {
                PromotionObservedAllFilesFlushed = Directory
                    .EnumerateFiles(source, "*", SearchOption.AllDirectories)
                    .All(file => _flushedFiles.Contains(Path.GetFullPath(file)));
                Assert.True(
                    PromotionObservedAllFilesFlushed,
                    "Promotion occurred before every staged file was durably flushed.");
                _promotionSource = source;
                _promotionDestination = destination;
            }

            _inner.MoveDirectory(source, destination);
        }

        public void DeleteFile(string path)
        {
            _events.Add(Path.GetFileName(path) == FileSystemPackageStore.CommitJournalFileName
                ? "delete-journal"
                : "delete-file");
            _inner.DeleteFile(path);
        }

        public void DeleteDirectory(string path, bool recursive)
        {
            bool backup = Path.GetFileName(path).Contains(".bak-", StringComparison.Ordinal);
            _events.Add(backup ? "delete-backup" : "delete-directory");
            if (backup && _remainingBackupDeleteFailures > 0)
            {
                _remainingBackupDeleteFailures--;
                throw new IOException("Injected backup deletion failure.");
            }

            _inner.DeleteDirectory(path, recursive);
            if (backup)
            {
                _lastDeletedBackup = path;
            }
        }

        public void FlushDirectory(string path)
        {
            _events.Add(string.Equals(Path.GetFullPath(path), Path.GetFullPath(root), PathComparison)
                ? "flush-root"
                : "flush-directory:" + Path.GetRelativePath(root, path));
            if (_promotionOccurred && _remainingPromotionFlushFailures > 0)
            {
                _remainingPromotionFlushFailures--;
                if (loseDestinationOnPromotionFlushFailure
                    && _promotionDestination is not null
                    && _inner.DirectoryExists(_promotionDestination))
                {
                    _inner.DeleteDirectory(_promotionDestination, recursive: true);
                }

                throw new IOException("Injected promotion durability barrier failure.");
            }

            if (_restoreOccurred && _remainingRestoreFlushFailures > 0)
            {
                _remainingRestoreFlushFailures--;
                throw new IOException("Injected rollback durability barrier failure.");
            }

            _inner.FlushDirectory(path);
        }

        public void CommitPoint(CommitFaultPoint point)
        {
            _events.Add("point:" + point);
            if (point == CommitFaultPoint.AfterPromotionBeforeDurable)
            {
                _promotionOccurred = true;
            }

            if (faultPoint != point)
            {
                return;
            }

            if (point == CommitFaultPoint.BeforeJournalDurable)
            {
                _inner.DeleteFile(Path.Combine(root, FileSystemPackageStore.CommitJournalFileName));
            }
            else if (point == CommitFaultPoint.AfterPromotionBeforeDurable
                && promotionCrashState == PromotionCrashState.DestinationLost
                && _promotionDestination is not null)
            {
                _inner.DeleteDirectory(_promotionDestination, recursive: true);
            }
            else if (point == CommitFaultPoint.AfterPromotionBeforeDurable
                && promotionCrashState == PromotionCrashState.DestinationAndStaging
                && _promotionSource is not null
                && _promotionDestination is not null)
            {
                CopyDirectory(_promotionDestination, _promotionSource);
            }
            else if (point == CommitFaultPoint.AfterBackupCleanupBeforeDurable
                && _lastDeletedBackup is not null)
            {
                Directory.CreateDirectory(_lastDeletedBackup);
            }

            throw new SimulatedProcessCrashException();
        }

        public int IndexOf(string value, int startIndex = 0)
        {
            int index = _events.FindIndex(startIndex, item => item == value);
            Assert.True(index >= 0, $"Missing durable-filesystem event '{value}'.");
            return index;
        }

        public int IndexOf(CommitFaultPoint point) => IndexOf("point:" + point);

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string directory in Directory.EnumerateDirectories(
                source,
                "*",
                SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(
                    destination,
                    Path.GetRelativePath(source, directory)));
            }

            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
            }
        }

        private static StringComparison PathComparison => OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    private enum PromotionCrashState
    {
        Persisted,
        DestinationLost,
        DestinationAndStaging,
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
