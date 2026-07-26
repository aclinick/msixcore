using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using MsixCore.Packaging;

namespace MsixCore.PackageStore;

/// <summary>
/// A self-contained, cross-platform <see cref="IPackageStore"/> that keeps each installed package as
/// an unpacked subdirectory of a store root. A subdirectory is treated as an installed package when
/// it contains an <c>AppxManifest.xml</c>.
/// </summary>
public sealed class FileSystemPackageStore : IPackageStore
{
    /// <summary>The default store-root directory name placed under the user's local application data.</summary>
    public const string DefaultStoreFolderName = "MsixCore/Packages";

    private readonly string _root;
    private readonly Func<string, IEnumerable<string>> _enumerateDirectories;
    private readonly IDurableFileSystem _fileSystem;

    /// <summary>Creates a store rooted at the given directory (created on demand).</summary>
    /// <param name="rootDirectory">The store-root directory that holds unpacked package folders.</param>
    /// <exception cref="ArgumentException"><paramref name="rootDirectory"/> is null or empty.</exception>
    public FileSystemPackageStore(string rootDirectory)
        : this(rootDirectory, Directory.EnumerateDirectories, DurableFileSystem.Instance)
    {
    }

    internal FileSystemPackageStore(
        string rootDirectory,
        Func<string, IEnumerable<string>> enumerateDirectories,
        IDurableFileSystem? fileSystem = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootDirectory);
        ArgumentNullException.ThrowIfNull(enumerateDirectories);
        _root = Path.GetFullPath(rootDirectory);
        _enumerateDirectories = enumerateDirectories;
        _fileSystem = fileSystem ?? DurableFileSystem.Instance;
    }

    /// <summary>The absolute store-root directory.</summary>
    public string RootDirectory => _root;

    /// <summary>Creates a store at the default per-user location.</summary>
    /// <returns>A <see cref="FileSystemPackageStore"/> under the local application data folder.</returns>
    public static FileSystemPackageStore CreateDefault()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new FileSystemPackageStore(Path.Combine(appData, "MsixCore", "Packages"));
    }

    /// <summary>The store subdirectory used for in-progress extraction; excluded from enumeration.</summary>
    private const string StagingFolderName = ".staging";
    internal const string CommitLockFileName = ".commit.lock";
    internal const string CommitJournalFileName = ".commit-transaction.json";

    /// <inheritdoc/>
    public IReadOnlyList<InstalledPackageInfo> EnumeratePackages()
    {
        using FileStream storeLock = AcquireCommitLock();
        RecoverIncompleteCommitLocked();
        return EnumeratePackagesLocked();
    }

    private List<InstalledPackageInfo> EnumeratePackagesLocked()
    {
        var packages = new List<InstalledPackageInfo>();
        foreach (string directory in EnumeratePackageDirectories())
        {
            try
            {
                packages.Add(InstalledPackageInfo.ReadFromDirectory(directory));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                continue;
            }
        }

        return packages
            .OrderBy(static package => package.Identity.PackageFullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc/>
    public InstalledPackageInfo? FindByFullName(string packageFullName)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageFullName);
        using FileStream storeLock = AcquireCommitLock();
        RecoverIncompleteCommitLocked();
        return FindByFullNameLocked(packageFullName);
    }

    /// <inheritdoc/>
    public InstalledPackageInfo? FindByFamilyName(string packageFamilyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageFamilyName);
        using FileStream storeLock = AcquireCommitLock();
        RecoverIncompleteCommitLocked();
        return EnumeratePackageInfos()
            .Where(package => string.Equals(
                package.Identity.PackageFamilyName,
                packageFamilyName,
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static package => package.Identity.Version)
            .ThenBy(static package => package.Identity.PackageFullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <inheritdoc/>
    public string GetInstallLocation(string packageFullName) =>
        Path.Combine(_root, ValidateFolderName(packageFullName));

    /// <inheritdoc/>
    public bool Contains(string packageFullName)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageFullName);
        using FileStream storeLock = AcquireCommitLock();
        RecoverIncompleteCommitLocked();
        return FindByFullNameLocked(packageFullName) is not null;
    }

    /// <inheritdoc/>
    public void Delete(string packageFullName)
    {
        using FileStream storeLock = AcquireCommitLock();
        RecoverIncompleteCommitLocked();
        string location = GetInstallLocation(packageFullName);
        if (Directory.Exists(location))
        {
            Directory.Delete(location, recursive: true);
        }
    }

    /// <inheritdoc/>
    public string CreateStagingLocation()
    {
        string staging = Path.Combine(_root, StagingFolderName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        return staging;
    }

    /// <inheritdoc/>
    public void Commit(string stagingLocation, InstalledPackageInfo package, DeploymentOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(stagingLocation);
        ArgumentNullException.ThrowIfNull(package);
        string staging = ValidateStagingLocation(stagingLocation, package);
        using FileStream storeLock = AcquireCommitLock();
        RecoverIncompleteCommitLocked();
        CommitLocked(staging, package, options);
    }

    private void CommitLocked(
        string stagingLocation,
        InstalledPackageInfo package,
        DeploymentOptions options)
    {
        PackageIdentity identity = package.Identity;
        string packageFullName = identity.PackageFullName;
        string destination = GetInstallLocation(packageFullName);
        Directory.CreateDirectory(_root);

        List<InstalledPackageInfo> familyPackages = EnumeratePackageInfosForCommit()
            .Where(installed => string.Equals(
                installed.Identity.PackageFamilyName,
                identity.PackageFamilyName,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        InstalledPackageInfo? exact = familyPackages.FirstOrDefault(installed => string.Equals(
            installed.Identity.PackageFullName,
            packageFullName,
            StringComparison.OrdinalIgnoreCase));
        if (exact is not null && !options.HasFlag(DeploymentOptions.ForceReinstall))
        {
            throw new InvalidOperationException(
                $"Package '{packageFullName}' is already installed. Use ForceReinstall to reinstall it.");
        }

        // Only the packages this one actually supersedes are replaced. A family legitimately holds
        // several packages at once — architecture variants, resource packages, and side-by-side
        // framework versions — and replacing the whole family would evict them.
        List<InstalledPackageInfo> replaced = familyPackages
            .Where(installed => IsSupersededBy(installed, package))
            .ToList();

        InstalledPackageInfo? newest = replaced
            .OrderByDescending(static installed => installed.Identity.Version)
            .ThenBy(static installed => installed.Identity.PackageFullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (newest is not null
            && identity.Version < newest.Identity.Version
            && !options.HasFlag(DeploymentOptions.AllowDowngrade))
        {
            throw new InvalidOperationException(
                $"Package family '{identity.PackageFamilyName}' has newer version '{newest.Identity.Version}' installed. "
                + "Use AllowDowngrade to replace it with an older version.");
        }

        MakeStagingDurable(stagingLocation);
        var transaction = new CommitTransaction
        {
            RecoveryMode = CommitRecoveryMode.RollForward,
            StagingRelativePath = Path.GetRelativePath(_root, stagingLocation),
            DestinationName = Path.GetFileName(destination),
            Backups = replaced.Select(installed => new CommitBackup
            {
                OriginalName = Path.GetFileName(installed.InstalledLocation),
                BackupName = "." + Path.GetFileName(installed.InstalledLocation)
                    + ".bak-" + Guid.NewGuid().ToString("N"),
            }).ToList(),
        };
        WriteCommitJournal(transaction);

        try
        {
            ExecuteCommitTransaction(transaction);
        }
        catch (SimulatedProcessCrashException)
        {
            throw;
        }
        catch (Exception commitFailure)
        {
            CommitRecoveryOutcome recoveryOutcome;
            try
            {
                recoveryOutcome = RecoverIncompleteCommitLocked();
            }
            catch (Exception recoveryFailure)
            {
                throw new AggregateException(
                    "The package commit failed and recovery was interrupted; the durable journal remains for retry.",
                    commitFailure,
                    recoveryFailure);
            }

            if (recoveryOutcome is CommitRecoveryOutcome.NoJournal or CommitRecoveryOutcome.RolledForward)
            {
                return;
            }

            ExceptionDispatchInfo.Capture(commitFailure).Throw();
            throw;
        }
    }

    private void ExecuteCommitTransaction(CommitTransaction transaction)
    {
        ResolvedCommitTransaction resolved = ResolveTransaction(transaction);
        MoveOriginalsToBackups(resolved);
        _fileSystem.FlushDirectory(_root);
        _fileSystem.CommitPoint(CommitFaultPoint.AfterBackupsDurableBeforePromotion);

        _fileSystem.MoveDirectory(resolved.StagingLocation, resolved.Destination);
        _fileSystem.CommitPoint(CommitFaultPoint.AfterPromotionBeforeDurable);
        FlushPromotionDirectories(resolved);
        _fileSystem.CommitPoint(CommitFaultPoint.AfterPromotionDurableBeforeJournalClear);
        CompleteCommitTransaction(resolved);
    }

    private CommitRecoveryOutcome RecoverIncompleteCommitLocked()
    {
        string journalPath = Path.Combine(_root, CommitJournalFileName);
        if (!_fileSystem.FileExists(journalPath))
        {
            return CommitRecoveryOutcome.NoJournal;
        }

        CommitTransaction transaction;
        try
        {
            using FileStream journal = File.OpenRead(journalPath);
            CommitJournal envelope = JsonSerializer.Deserialize<CommitJournal>(journal)
                ?? throw MsixError.Format(MsixErrorCode.PackageStore, "The commit journal is empty.");
            transaction = ReadCommitJournal(envelope);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            throw MsixError.Tag(
                new InvalidOperationException(
                    $"The package store commit journal '{journalPath}' cannot be recovered.",
                    ex),
                MsixErrorCode.PackageStore);
        }

        ResolvedCommitTransaction resolved = ResolveTransaction(transaction);
        if (transaction.RecoveryMode is not (
            CommitRecoveryMode.RollForward or CommitRecoveryMode.RollBack))
        {
            throw MsixError.Tag(
                new InvalidOperationException(
                    $"The commit journal has unsupported recovery mode '{transaction.RecoveryMode}'."),
                MsixErrorCode.PackageStore);
        }

        return ConvergeCommitTransaction(transaction, resolved);
    }

    private CommitRecoveryOutcome ConvergeCommitTransaction(
        CommitTransaction transaction,
        ResolvedCommitTransaction resolved)
    {
        if (transaction.RecoveryMode == CommitRecoveryMode.RollBack)
        {
            ConvergeRollback(resolved);
            return CommitRecoveryOutcome.RolledBack;
        }

        // Staging present means promotion has not completed. Destination present means promotion
        // occurred, but its durability barrier may still need to be replayed.
        if (_fileSystem.DirectoryExists(resolved.StagingLocation))
        {
            if (_fileSystem.DirectoryExists(resolved.Destination))
            {
                ChangeRecoveryMode(transaction, CommitRecoveryMode.RollBack);
                ConvergeRollback(resolved);
                return CommitRecoveryOutcome.RolledBack;
            }

            MoveOriginalsToBackups(resolved);
            _fileSystem.FlushDirectory(_root);
            if (!_fileSystem.DirectoryExists(resolved.StagingLocation))
            {
                throw MsixError.Tag(
                    new InvalidOperationException(
                        $"Staging disappeared while moving backups: '{resolved.StagingLocation}'. Backups: "
                        + string.Join(", ", resolved.Backups.Select(static backup => backup.Original))),
                    MsixErrorCode.PackageStore);
            }

            _fileSystem.MoveDirectory(resolved.StagingLocation, resolved.Destination);
        }
        else if (!_fileSystem.DirectoryExists(resolved.Destination))
        {
            ChangeRecoveryMode(transaction, CommitRecoveryMode.RollBack);
            ConvergeRollback(resolved);
            return CommitRecoveryOutcome.RolledBack;
        }

        FlushPromotionDirectories(resolved);
        CompleteCommitTransaction(resolved);
        return CommitRecoveryOutcome.RolledForward;
    }

    private void ConvergeRollback(ResolvedCommitTransaction resolved)
    {
        if (_fileSystem.DirectoryExists(resolved.Destination))
        {
            _fileSystem.DeleteDirectory(resolved.Destination, recursive: true);
            _fileSystem.FlushDirectory(_root);
        }

        if (_fileSystem.DirectoryExists(resolved.StagingLocation))
        {
            _fileSystem.DeleteDirectory(resolved.StagingLocation, recursive: true);
            _fileSystem.FlushDirectory(Path.GetDirectoryName(resolved.StagingLocation)!);
        }

        RestoreBackups(resolved);
        _fileSystem.FlushDirectory(_root);
        CompleteCommitTransaction(resolved);
    }

    private void WriteCommitJournal(CommitTransaction transaction)
    {
        string journalPath = Path.Combine(_root, CommitJournalFileName);
        if (_fileSystem.FileExists(journalPath))
        {
            throw MsixError.Tag(
                new InvalidOperationException(
                    $"The package store contains an unrecovered commit journal at '{journalPath}'."),
                MsixErrorCode.PackageStore);
        }

        string temporary = Path.Combine(_root, ".commit-transaction-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            WriteCommitJournalFile(temporary, transaction);
            _fileSystem.MoveFile(temporary, journalPath);
            _fileSystem.CommitPoint(CommitFaultPoint.BeforeJournalDurable);
            _fileSystem.FlushDirectory(_root);
            _fileSystem.CommitPoint(CommitFaultPoint.AfterJournalDurableBeforeBackups);
        }
        finally
        {
            if (_fileSystem.FileExists(temporary))
            {
                _fileSystem.DeleteFile(temporary);
            }
        }
    }

    private void ChangeRecoveryMode(
        CommitTransaction transaction,
        CommitRecoveryMode recoveryMode)
    {
        transaction.RecoveryMode = recoveryMode;
        string temporary = Path.Combine(_root, ".commit-transaction-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            WriteCommitJournalFile(temporary, transaction);
            _fileSystem.ReplaceFile(temporary, Path.Combine(_root, CommitJournalFileName));
            _fileSystem.FlushDirectory(_root);
        }
        finally
        {
            if (_fileSystem.FileExists(temporary))
            {
                _fileSystem.DeleteFile(temporary);
            }
        }
    }

    private static void WriteCommitJournalFile(
        string path,
        CommitTransaction transaction)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(transaction);
        var journal = new CommitJournal
        {
            FormatVersion = 1,
            Payload = Convert.ToBase64String(payload),
            Sha256 = Convert.ToHexString(SHA256.HashData(payload)),
        };
        JsonSerializer.Serialize(stream, journal);
        stream.Flush(flushToDisk: true);
    }

    private void MakeStagingDurable(string stagingLocation)
    {
        var pending = new Stack<string>();
        var directories = new List<string>();
        pending.Push(stagingLocation);

        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            directories.Add(directory);
            foreach (string entry in _fileSystem.EnumerateFileSystemEntries(directory))
            {
                FileAttributes attributes = _fileSystem.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw MsixError.Format(MsixErrorCode.PackageStore,
                        $"Staging path '{entry}' is a symbolic link or junction; refusing to commit.");
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    pending.Push(entry);
                }
                else
                {
                    _fileSystem.FlushFile(entry);
                    _fileSystem.CommitPoint(CommitFaultPoint.MidStagedFileFlush);
                }
            }
        }

        _fileSystem.CommitPoint(CommitFaultPoint.AfterStagedFilesBeforeDirectoryFlush);
        foreach (string directory in directories.AsEnumerable().Reverse())
        {
            _fileSystem.FlushDirectory(directory);
        }

        _fileSystem.FlushDirectory(Path.GetDirectoryName(stagingLocation)!);
        _fileSystem.FlushDirectory(_root);
        _fileSystem.CommitPoint(CommitFaultPoint.AfterStagingDurableBeforeJournal);
    }

    private static CommitTransaction ReadCommitJournal(CommitJournal journal)
    {
        if (journal.FormatVersion != 1)
        {
            throw MsixError.Format(MsixErrorCode.PackageStore,
                $"The commit journal has unsupported format version '{journal.FormatVersion}'.");
        }

        if (string.IsNullOrEmpty(journal.Payload) || string.IsNullOrEmpty(journal.Sha256))
        {
            throw MsixError.Format(MsixErrorCode.PackageStore, "The commit journal is missing integrity data.");
        }

        try
        {
            byte[] payload = Convert.FromBase64String(journal.Payload);
            byte[] expectedHash = Convert.FromHexString(journal.Sha256);
            byte[] actualHash = SHA256.HashData(payload);
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
            {
                throw MsixError.Format(MsixErrorCode.PackageStore, "The commit journal failed its integrity check.");
            }

            return JsonSerializer.Deserialize<CommitTransaction>(payload)
                ?? throw MsixError.Format(MsixErrorCode.PackageStore, "The commit journal transaction is empty.");
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw MsixError.Format(MsixErrorCode.PackageStore, "The commit journal is malformed.", ex);
        }
    }

    private void FlushPromotionDirectories(ResolvedCommitTransaction transaction)
    {
        _fileSystem.FlushDirectory(Path.GetDirectoryName(transaction.StagingLocation)!);
        _fileSystem.FlushDirectory(_root);
    }

    private void MoveOriginalsToBackups(ResolvedCommitTransaction transaction)
    {
        foreach ((string original, string backup) in transaction.Backups)
        {
            bool originalExists = _fileSystem.DirectoryExists(original);
            bool backupExists = _fileSystem.DirectoryExists(backup);
            if (originalExists && !backupExists)
            {
                _fileSystem.MoveDirectory(original, backup);
                _fileSystem.CommitPoint(CommitFaultPoint.MidBackup);
            }
            else if (originalExists == backupExists)
            {
                throw MsixError.Tag(
                    new InvalidOperationException(
                        $"Cannot continue package-store commit: expected exactly one of '{original}' or '{backup}' to exist."),
                    MsixErrorCode.PackageStore);
            }
        }
    }

    private void RestoreBackups(ResolvedCommitTransaction transaction)
    {
        foreach ((string original, string backup) in transaction.Backups.AsEnumerable().Reverse())
        {
            if (!_fileSystem.DirectoryExists(original) && _fileSystem.DirectoryExists(backup))
            {
                _fileSystem.MoveDirectory(backup, original);
            }
        }
    }

    private void CleanupBackups(ResolvedCommitTransaction transaction)
    {
        foreach ((_, string backup) in transaction.Backups)
        {
            if (!_fileSystem.DirectoryExists(backup))
            {
                continue;
            }

            _fileSystem.DeleteDirectory(backup, recursive: true);
            if (_fileSystem.DirectoryExists(backup))
            {
                throw new IOException($"Could not remove package-store backup '{backup}'.");
            }
        }
    }

    private void CompleteCommitTransaction(ResolvedCommitTransaction transaction)
    {
        CleanupBackups(transaction);
        _fileSystem.CommitPoint(CommitFaultPoint.AfterBackupCleanupBeforeDurable);
        _fileSystem.FlushDirectory(_root);
        DeleteCommitJournal();
        _fileSystem.FlushDirectory(_root);
    }

    private void DeleteCommitJournal()
    {
        _fileSystem.DeleteFile(Path.Combine(_root, CommitJournalFileName));
    }

    private ResolvedCommitTransaction ResolveTransaction(CommitTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ValidateTransactionName(transaction.DestinationName, nameof(transaction.DestinationName));

        string staging = Path.GetFullPath(Path.Combine(_root, transaction.StagingRelativePath));
        string stagingRoot = Path.GetFullPath(Path.Combine(_root, StagingFolderName));
        if (!IsDescendant(stagingRoot, staging))
        {
            throw MsixError.Tag(new InvalidOperationException("The commit journal contains an invalid staging location."), MsixErrorCode.PackageStore);
        }

        var backups = new List<(string Original, string Backup)>(transaction.Backups.Count);
        foreach (CommitBackup item in transaction.Backups)
        {
            ValidateTransactionName(item.OriginalName, nameof(item.OriginalName));
            ValidateTransactionName(item.BackupName, nameof(item.BackupName));
            if (!item.BackupName.StartsWith('.'))
            {
                throw MsixError.Tag(new InvalidOperationException("The commit journal contains an invalid backup name."), MsixErrorCode.PackageStore);
            }

            backups.Add((
                Path.Combine(_root, item.OriginalName),
                Path.Combine(_root, item.BackupName)));
        }

        return new ResolvedCommitTransaction
        {
            StagingLocation = staging,
            Destination = Path.Combine(_root, transaction.DestinationName),
            Backups = backups,
        };
    }

    private static void ValidateTransactionName(string name, string propertyName)
    {
        if (string.IsNullOrEmpty(name)
            || name is "." or ".."
            || !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal))
        {
            throw MsixError.Tag(new InvalidOperationException($"The commit journal contains an invalid {propertyName}."), MsixErrorCode.PackageStore);
        }
    }

    private static string ValidateFolderName(string packageFullName)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageFullName);

        // A package full name must be a single path segment; reject anything that could traverse.
        if (packageFullName.Any(static c =>
                char.IsControl(c) || c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')
            || packageFullName is "." or ".."
            || packageFullName.EndsWith(' ')
            || packageFullName.EndsWith('.'))
        {
            throw new ArgumentException($"Invalid package full name: '{packageFullName}'.", nameof(packageFullName));
        }

        return packageFullName;
    }

    private string ValidateStagingLocation(string stagingLocation, InstalledPackageInfo package)
    {
        string staging = Path.GetFullPath(stagingLocation);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(staging, Path.GetFullPath(package.InstalledLocation), comparison))
        {
            throw new ArgumentException(
                "The package metadata must have been read from the staging location.",
                nameof(package));
        }

        string stagingRoot = Path.GetFullPath(Path.Combine(_root, StagingFolderName));
        string stagingPrefix = stagingRoot.EndsWith(Path.DirectorySeparatorChar)
            ? stagingRoot
            : stagingRoot + Path.DirectorySeparatorChar;
        if (!staging.StartsWith(stagingPrefix, comparison))
        {
            throw new ArgumentException(
                "The staging location must be created by this package store.",
                nameof(stagingLocation));
        }

        return staging;
    }

    private static bool ContainsManifest(string directory)
    {
        return InstalledPackageInfo.FindManifest(directory) is not null;
    }

    private InstalledPackageInfo? FindByFullNameLocked(string packageFullName)
    {
        string location = GetInstallLocation(packageFullName);
        InstalledPackageInfo? direct = TryReadInfo(location);
        if (direct is not null)
        {
            return HasFullName(direct, packageFullName) ? direct : null;
        }

        if (!Directory.Exists(_root))
        {
            return null;
        }

        string? caseInsensitiveMatch = _enumerateDirectories(_root)
            .Where(directory => string.Equals(
                Path.GetFileName(directory),
                packageFullName,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(static directory => directory, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static directory => directory, StringComparer.Ordinal)
            .FirstOrDefault();
        InstalledPackageInfo? fallback = caseInsensitiveMatch is null
            ? null
            : TryReadInfo(caseInsensitiveMatch);
        return fallback is not null && HasFullName(fallback, packageFullName) ? fallback : null;
    }

    private static bool HasFullName(InstalledPackageInfo package, string packageFullName) =>
        string.Equals(
            package.Identity.PackageFullName,
            packageFullName,
            StringComparison.OrdinalIgnoreCase);

    private IEnumerable<string> EnumeratePackageDirectories()
    {
        if (!Directory.Exists(_root))
        {
            yield break;
        }

        foreach (string directory in _enumerateDirectories(_root))
        {
            if (!Path.GetFileName(directory).StartsWith('.') && ContainsManifest(directory))
            {
                yield return directory;
            }
        }
    }

    private IEnumerable<InstalledPackageInfo> EnumeratePackageInfos()
    {
        foreach (string directory in EnumeratePackageDirectories())
        {
            InstalledPackageInfo? info = TryReadInfo(directory);
            if (info is not null)
            {
                yield return info;
            }
        }
    }

    private IEnumerable<InstalledPackageInfo> EnumeratePackageInfosForCommit()
    {
        if (!Directory.Exists(_root))
        {
            yield break;
        }

        List<string> directories;
        try
        {
            directories = _enumerateDirectories(_root)
                .Where(static directory => !Path.GetFileName(directory).StartsWith('.'))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Installed-package metadata could not be enumerated; the commit was aborted.",
                ex);
        }

        foreach (string directory in directories)
        {
            InstalledPackageInfo? info = ReadInfoForCommit(directory);
            if (info is not null)
            {
                yield return info;
            }
        }
    }

    /// <summary>
    /// Whether committing <paramref name="incoming"/> replaces the already-installed
    /// <paramref name="installed"/> package from the same family.
    /// </summary>
    /// <remarks>
    /// A package family is not a single slot. Windows keeps architecture variants of a framework
    /// side by side (an x86 app and an x64 app on one machine each need their own build of
    /// <c>Microsoft.VCLibs</c>), keeps resource packages alongside the main package, and keeps
    /// multiple framework versions installed at once because each app binds to the specific
    /// <c>MinVersion</c> it declared. Only a package with the same architecture and resource id —
    /// and, for frameworks, the same version — is genuinely superseded.
    /// </remarks>
    private static bool IsSupersededBy(InstalledPackageInfo installed, InstalledPackageInfo incoming)
    {
        if (installed.Identity.Architecture != incoming.Identity.Architecture)
        {
            return false;
        }

        if (!string.Equals(
                installed.Identity.ResourceId,
                incoming.Identity.ResourceId,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (installed.IsFramework || incoming.IsFramework)
        {
            return installed.Identity.Version == incoming.Identity.Version;
        }

        return true;
    }

    private static InstalledPackageInfo? ReadInfoForCommit(string directory)
    {
        try
        {
            if (InstalledPackageInfo.FindManifestStrict(directory) is null)
            {
                return null;
            }

            return InstalledPackageInfo.ReadFromDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new InvalidOperationException(
                $"Installed-package metadata at '{directory}' could not be read; the commit was aborted.",
                ex);
        }
    }

    private static InstalledPackageInfo? TryReadInfo(string directory)
    {
        if (!ContainsManifest(directory))
        {
            return null;
        }

        try
        {
            return InstalledPackageInfo.ReadFromDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    private FileStream AcquireCommitLock()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, CommitLockFileName);
        var timeout = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (timeout.Elapsed < TimeSpan.FromSeconds(30))
            {
                Thread.Sleep(25);
            }
        }
    }

    private static bool IsDescendant(string root, string path)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, comparison);
    }

    private sealed class CommitTransaction
    {
        public CommitRecoveryMode RecoveryMode { get; set; }

        public required string StagingRelativePath { get; init; }

        public required string DestinationName { get; init; }

        public required List<CommitBackup> Backups { get; init; }
    }

    private sealed class CommitJournal
    {
        public int FormatVersion { get; init; }

        public required string Payload { get; init; }

        public required string Sha256 { get; init; }
    }

    private sealed class CommitBackup
    {
        public required string OriginalName { get; init; }

        public required string BackupName { get; init; }
    }

    private sealed class ResolvedCommitTransaction
    {
        public required string StagingLocation { get; init; }

        public required string Destination { get; init; }

        public required List<(string Original, string Backup)> Backups { get; init; }
    }
}

internal enum CommitFaultPoint
{
    MidStagedFileFlush,
    AfterStagedFilesBeforeDirectoryFlush,
    AfterStagingDurableBeforeJournal,
    BeforeJournalDurable,
    AfterJournalDurableBeforeBackups,
    MidBackup,
    AfterBackupsDurableBeforePromotion,
    AfterPromotionBeforeDurable,
    AfterPromotionDurableBeforeJournalClear,
    AfterBackupCleanupBeforeDurable,
}

internal enum CommitRecoveryMode
{
    RollForward,
    RollBack,
}

internal enum CommitRecoveryOutcome
{
    NoJournal,
    RolledForward,
    RolledBack,
}

internal sealed class SimulatedProcessCrashException : Exception
{
}
