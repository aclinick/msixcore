using MsixCore.Packaging;

namespace MsixCore.Deployment;

/// <summary>
/// Abstraction over where installed packages are recorded and their unpacked payloads live. The
/// default cross-platform implementation is a self-contained store root
/// (<see cref="FileSystemPackageStore"/>); a Windows OS-integrated store can be added later without
/// changing the query surface.
/// </summary>
public interface IPackageStore
{
    /// <summary>
    /// Enumerates installed-package metadata without indexing package payload files.
    /// </summary>
    IReadOnlyList<InstalledPackageInfo> EnumeratePackages();

    /// <summary>Finds installed-package metadata by exact package full name.</summary>
    InstalledPackageInfo? FindByFullName(string packageFullName);

    /// <summary>Finds installed-package metadata by package family name.</summary>
    InstalledPackageInfo? FindByFamilyName(string packageFamilyName);

    /// <summary>
    /// Returns the directory where the given package's payload lives (or would live). The directory
    /// may not exist yet.
    /// </summary>
    /// <param name="packageFullName">The package full name.</param>
    string GetInstallLocation(string packageFullName);

    /// <summary>Returns whether a package with the given full name is currently installed.</summary>
    /// <param name="packageFullName">The package full name.</param>
    bool Contains(string packageFullName);

    /// <summary>Removes the installed package's payload. No-op if it is not installed.</summary>
    /// <param name="packageFullName">The package full name.</param>
    void Delete(string packageFullName);

    /// <summary>Creates a fresh, empty staging directory that <see cref="Commit"/> can promote.</summary>
    /// <returns>The absolute path to a new staging directory.</returns>
    string CreateStagingLocation();

    /// <summary>
    /// Atomically promotes a staging directory (from <see cref="CreateStagingLocation"/>) to the
    /// install location for the given package, replacing any existing payload.
    /// </summary>
    /// <param name="stagingLocation">A staging directory previously created by this store.</param>
    /// <param name="package">Metadata read from the verified staging layout.</param>
    /// <param name="options">Options controlling version replacement policy.</param>
    void Commit(string stagingLocation, InstalledPackageInfo package, DeploymentOptions options);
}
