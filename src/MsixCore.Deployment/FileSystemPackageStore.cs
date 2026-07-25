using System.Collections.Concurrent;
using MsixCore.Packaging;
using MsixCore.Packaging.Opc;

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

    /// <summary>Creates a store rooted at the given directory (created on demand).</summary>
    /// <param name="rootDirectory">The store-root directory that holds unpacked package folders.</param>
    /// <exception cref="ArgumentException"><paramref name="rootDirectory"/> is null or empty.</exception>
    public FileSystemPackageStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootDirectory);
        _root = Path.GetFullPath(rootDirectory);
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

    // Serializes the move-aside / promote / rollback sequence per install location so two concurrent
    // commits of the same package cannot interleave — otherwise one commit's rollback could delete the
    // other commit's already-promoted installation. Keyed by absolute destination path (process-wide)
    // so it also covers separate store instances over the same root. NOTE: this guards a single process
    // only; cross-process coordination over a shared store root is tracked separately (see issue #14).
    private static readonly ConcurrentDictionary<string, object> PromotionGates =
        new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public IReadOnlyList<IInstalledPackage> EnumeratePackages()
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

        var packages = new List<IInstalledPackage>();
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(_root))
            {
                // Skip reserved/internal directories (e.g. staging) so partial installs aren't listed.
                if (Path.GetFileName(directory).StartsWith('.'))
                {
                    continue;
                }

                if (!File.Exists(Path.Combine(directory, OpcPartNames.AppxManifest)))
                {
                    continue;
                }

                packages.Add(InstalledPackage.OpenDirectory(directory));
            }
        }
        catch
        {
            // Dispose anything opened before the failure to avoid leaks, then rethrow.
            foreach (IInstalledPackage package in packages)
            {
                package.Dispose();
            }

            throw;
        }

        return packages;
    }

    /// <inheritdoc/>
    public string GetInstallLocation(string packageFullName) =>
        Path.Combine(_root, ValidateFolderName(packageFullName));

    /// <inheritdoc/>
    public bool Contains(string packageFullName) =>
        File.Exists(Path.Combine(GetInstallLocation(packageFullName), OpcPartNames.AppxManifest));

    /// <inheritdoc/>
    public void Delete(string packageFullName)
    {
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
    public void Commit(string stagingLocation, string packageFullName)
    {
        ArgumentException.ThrowIfNullOrEmpty(stagingLocation);
        string destination = GetInstallLocation(packageFullName);

        // Serialize the whole aside/promote/rollback transaction for this destination so a concurrent
        // commit of the same package cannot observe or clobber our intermediate state.
        lock (PromotionGates.GetOrAdd(destination, static _ => new object()))
        {
            CommitLocked(stagingLocation, destination, packageFullName);
        }
    }

    private void CommitLocked(string stagingLocation, string destination, string packageFullName)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        // Move any existing installation aside (rather than deleting it) so a failed promotion can be
        // rolled back instead of destroying the previously-installed package. The backup name starts
        // with '.' so it is excluded from EnumeratePackages while it exists.
        string? backup = null;
        if (Directory.Exists(destination))
        {
            backup = Path.Combine(_root, "." + packageFullName + ".bak-" + Guid.NewGuid().ToString("N"));
            Directory.Move(destination, backup);
        }

        try
        {
            Directory.Move(stagingLocation, destination);
        }
        catch
        {
            // Promotion failed: restore the previous installation, if any.
            if (backup is not null)
            {
                if (Directory.Exists(destination))
                {
                    Directory.Delete(destination, recursive: true);
                }

                Directory.Move(backup, destination);
            }

            throw;
        }

        // The new installation is already in place and the operation has succeeded. Removing the
        // backup is pure cleanup, so a failure here (e.g. a transient lock on the old files) must not
        // fail an otherwise-successful install. Leave the stale ('.'-prefixed, enumeration-excluded)
        // backup behind for later cleanup rather than reporting a false failure.
        if (backup is not null && Directory.Exists(backup))
        {
            try
            {
                Directory.Delete(backup, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup; the successful promotion stands.
            }
        }
    }

    private static string ValidateFolderName(string packageFullName)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageFullName);

        // A package full name must be a single path segment; reject anything that could traverse.
        if (packageFullName.Contains(Path.DirectorySeparatorChar)
            || packageFullName.Contains(Path.AltDirectorySeparatorChar)
            || packageFullName is "." or ".."
            || packageFullName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"Invalid package full name: '{packageFullName}'.", nameof(packageFullName));
        }

        return packageFullName;
    }
}
