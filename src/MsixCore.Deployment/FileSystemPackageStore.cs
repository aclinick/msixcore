using System.Diagnostics;
using System.Text.Json;
using MsixCore.Packaging;

namespace MsixCore.Deployment;

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
    private readonly Func<CommitFaultPoint, bool>? _faultInjector;

    /// <summary>Creates a store rooted at the given directory (created on demand).</summary>
    /// <param name="rootDirectory">The store-root directory that holds unpacked package folders.</param>
    /// <exception cref="ArgumentException"><paramref name="rootDirectory"/> is null or empty.</exception>
    public FileSystemPackageStore(string rootDirectory)
        : this(rootDirectory, Directory.EnumerateDirectories, null)
    {
    }

    internal FileSystemPackageStore(
        string rootDirectory,
        Func<string, IEnumerable<string>> enumerateDirectories,
        Func<CommitFaultPoint, bool>? faultInjector = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootDirectory);
        ArgumentNullException.ThrowIfNull(enumerateDirectories);
        _root = Path.GetFullPath(rootDirectory);
        _enumerateDirectories = enumerateDirectories;
        _faultInjector = faultInjector;
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

        InstalledPackageInfo? newest = familyPackages
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

        var transaction = new CommitTransaction
        {
            StagingRelativePath = Path.GetRelativePath(_root, stagingLocation),
            DestinationName = Path.GetFileName(destination),
            Backups = familyPackages.Select(installed => new CommitBackup
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
        catch
        {
            RollBackCommitTransaction(transaction);
            DeleteCommitJournal();
            throw;
        }
    }

    private void ExecuteCommitTransaction(CommitTransaction transaction)
    {
        ResolvedCommitTransaction resolved = ResolveTransaction(transaction);
        MoveOriginalsToBackups(resolved);

        if (_faultInjector?.Invoke(CommitFaultPoint.AfterBackupsMovedBeforePromotion) == true)
        {
            throw new SimulatedProcessCrashException();
        }

        Directory.Move(resolved.StagingLocation, resolved.Destination);
        CleanupBackups(resolved);
        DeleteCommitJournal();
    }

    private void RecoverIncompleteCommitLocked()
    {
        string journalPath = Path.Combine(_root, CommitJournalFileName);
        if (!File.Exists(journalPath))
        {
            return;
        }

        CommitTransaction transaction;
        try
        {
            using FileStream journal = File.OpenRead(journalPath);
            transaction = JsonSerializer.Deserialize<CommitTransaction>(journal)
                ?? throw new InvalidDataException("The commit journal is empty.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            throw new InvalidOperationException(
                $"The package store commit journal '{journalPath}' cannot be recovered.",
                ex);
        }

        ResolvedCommitTransaction resolved = ResolveTransaction(transaction);
        // The staging-to-destination rename is atomic: staging present means promotion has not
        // completed, while staging absent plus destination present means it has.
        if (Directory.Exists(resolved.StagingLocation))
        {
            MoveOriginalsToBackups(resolved);
            if (Directory.Exists(resolved.Destination))
            {
                throw new InvalidOperationException(
                    $"Cannot recover package-store commit because destination '{resolved.Destination}' already exists.");
            }

            Directory.Move(resolved.StagingLocation, resolved.Destination);
        }
        else if (!Directory.Exists(resolved.Destination))
        {
            if (resolved.Backups.Count == 0)
            {
                throw new InvalidOperationException(
                    "Cannot recover package-store commit because both staging and destination are missing.");
            }

            RestoreBackups(resolved);
        }

        CleanupBackups(resolved);
        DeleteCommitJournal();
    }

    private void WriteCommitJournal(CommitTransaction transaction)
    {
        string journalPath = Path.Combine(_root, CommitJournalFileName);
        if (File.Exists(journalPath))
        {
            throw new InvalidOperationException(
                $"The package store contains an unrecovered commit journal at '{journalPath}'.");
        }

        string temporary = Path.Combine(_root, ".commit-transaction-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                JsonSerializer.Serialize(stream, transaction);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, journalPath);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private void RollBackCommitTransaction(CommitTransaction transaction)
    {
        ResolvedCommitTransaction resolved = ResolveTransaction(transaction);
        RestoreBackups(resolved);
    }

    private static void MoveOriginalsToBackups(ResolvedCommitTransaction transaction)
    {
        foreach ((string original, string backup) in transaction.Backups)
        {
            bool originalExists = Directory.Exists(original);
            bool backupExists = Directory.Exists(backup);
            if (originalExists && !backupExists)
            {
                Directory.Move(original, backup);
            }
            else if (originalExists == backupExists)
            {
                throw new InvalidOperationException(
                    $"Cannot continue package-store commit: expected exactly one of '{original}' or '{backup}' to exist.");
            }
        }
    }

    private static void RestoreBackups(ResolvedCommitTransaction transaction)
    {
        foreach ((string original, string backup) in transaction.Backups.AsEnumerable().Reverse())
        {
            if (!Directory.Exists(original) && Directory.Exists(backup))
            {
                Directory.Move(backup, original);
            }
        }
    }

    private static void CleanupBackups(ResolvedCommitTransaction transaction)
    {
        foreach ((_, string backup) in transaction.Backups)
        {
            if (!Directory.Exists(backup))
            {
                continue;
            }

            try
            {
                Directory.Delete(backup, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The promoted package is valid; stale dot-prefixed backups are ignored by queries.
            }
        }
    }

    private void DeleteCommitJournal()
    {
        try
        {
            File.Delete(Path.Combine(_root, CommitJournalFileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Recovery is idempotent and will retry journal cleanup on the next locked operation.
        }
    }

    private ResolvedCommitTransaction ResolveTransaction(CommitTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ValidateTransactionName(transaction.DestinationName, nameof(transaction.DestinationName));

        string staging = Path.GetFullPath(Path.Combine(_root, transaction.StagingRelativePath));
        string stagingRoot = Path.GetFullPath(Path.Combine(_root, StagingFolderName));
        if (!IsDescendant(stagingRoot, staging))
        {
            throw new InvalidOperationException("The commit journal contains an invalid staging location.");
        }

        var backups = new List<(string Original, string Backup)>(transaction.Backups.Count);
        foreach (CommitBackup item in transaction.Backups)
        {
            ValidateTransactionName(item.OriginalName, nameof(item.OriginalName));
            ValidateTransactionName(item.BackupName, nameof(item.BackupName));
            if (!item.BackupName.StartsWith('.'))
            {
                throw new InvalidOperationException("The commit journal contains an invalid backup name.");
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
            throw new InvalidOperationException($"The commit journal contains an invalid {propertyName}.");
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
        public required string StagingRelativePath { get; init; }

        public required string DestinationName { get; init; }

        public required List<CommitBackup> Backups { get; init; }
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
    AfterBackupsMovedBeforePromotion,
}

internal sealed class SimulatedProcessCrashException : Exception
{
}
