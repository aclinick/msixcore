using System.Diagnostics;
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

    /// <summary>Creates a store rooted at the given directory (created on demand).</summary>
    /// <param name="rootDirectory">The store-root directory that holds unpacked package folders.</param>
    /// <exception cref="ArgumentException"><paramref name="rootDirectory"/> is null or empty.</exception>
    public FileSystemPackageStore(string rootDirectory)
        : this(rootDirectory, Directory.EnumerateDirectories)
    {
    }

    internal FileSystemPackageStore(
        string rootDirectory,
        Func<string, IEnumerable<string>> enumerateDirectories)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootDirectory);
        ArgumentNullException.ThrowIfNull(enumerateDirectories);
        _root = Path.GetFullPath(rootDirectory);
        _enumerateDirectories = enumerateDirectories;
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

    /// <inheritdoc/>
    public IReadOnlyList<InstalledPackageInfo> EnumeratePackages()
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

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
        string location = GetInstallLocation(packageFullName);
        InstalledPackageInfo? direct = TryReadInfo(location);
        if (direct is not null || !Directory.Exists(_root))
        {
            return direct;
        }

        string? caseInsensitiveMatch = _enumerateDirectories(_root)
            .FirstOrDefault(directory => string.Equals(
                Path.GetFileName(directory),
                packageFullName,
                StringComparison.OrdinalIgnoreCase));
        return caseInsensitiveMatch is null ? null : TryReadInfo(caseInsensitiveMatch);
    }

    /// <inheritdoc/>
    public InstalledPackageInfo? FindByFamilyName(string packageFamilyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageFamilyName);
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
    public bool Contains(string packageFullName) =>
        ContainsManifest(GetInstallLocation(packageFullName));

    /// <inheritdoc/>
    public void Delete(string packageFullName)
    {
        using FileStream storeLock = AcquireCommitLock();
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

        List<InstalledPackageInfo> familyPackages = EnumeratePackageInfos()
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

        var backups = new List<(string Original, string Backup)>();
        bool promoted = false;
        try
        {
            foreach (InstalledPackageInfo installed in familyPackages)
            {
                string original = installed.InstalledLocation;
                string backup = Path.Combine(
                    _root,
                    "." + Path.GetFileName(original) + ".bak-" + Guid.NewGuid().ToString("N"));
                Directory.Move(original, backup);
                backups.Add((original, backup));
            }

            Directory.Move(stagingLocation, destination);
            promoted = true;
        }
        catch
        {
            if (promoted && Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }

            foreach ((string original, string backup) in backups.AsEnumerable().Reverse())
            {
                if (Directory.Exists(backup))
                {
                    Directory.Move(backup, original);
                }
            }

            throw;
        }

        foreach ((_, string backup) in backups)
        {
            if (Directory.Exists(backup))
            {
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
}
