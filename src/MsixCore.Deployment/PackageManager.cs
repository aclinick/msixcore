using MsixCore.Packaging;

namespace MsixCore.Deployment;

/// <summary>
/// Default <see cref="IPackageManager"/> implementation.
/// </summary>
/// <remarks>
/// Phase 4 implements the query surface over an <see cref="IPackageStore"/>. Add/remove land in
/// Phase 5 (extraction pipeline); Windows OS-integration handlers land in Phase 6.
/// </remarks>
public sealed class PackageManager : IPackageManager
{
    private readonly IPackageStore _store;

    /// <summary>Creates a package manager backed by the default per-user file-system store.</summary>
    public PackageManager()
        : this(FileSystemPackageStore.CreateDefault())
    {
    }

    /// <summary>Creates a package manager backed by the given store.</summary>
    /// <param name="store">The store used to enumerate and resolve installed packages.</param>
    public PackageManager(IPackageStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc/>
    public IMsixResponse AddPackage(
        string packageFilePath,
        DeploymentOptions options = DeploymentOptions.None,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Implemented in Phase 5 (deployment engine).");

    /// <inheritdoc/>
    public IMsixResponse RemovePackage(
        string packageFullName,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Implemented in Phase 5 (deployment engine).");

    /// <inheritdoc/>
    public IInstalledPackage? FindPackage(string packageFullName)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageFullName);
        return FindSingle(package =>
            string.Equals(package.Identity.PackageFullName, packageFullName, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc/>
    public IInstalledPackage? FindPackageByFamilyName(string packageFamilyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageFamilyName);
        return FindSingle(package =>
            string.Equals(package.Identity.PackageFamilyName, packageFamilyName, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc/>
    public IReadOnlyList<IInstalledPackage> FindPackages(string searchParameter)
    {
        ArgumentException.ThrowIfNullOrEmpty(searchParameter);

        var matches = new List<IInstalledPackage>();
        foreach (IInstalledPackage package in _store.EnumeratePackages())
        {
            if (Wildcard.IsMatch(searchParameter, package.Identity.PackageFullName))
            {
                matches.Add(package);
            }
            else
            {
                package.Dispose();
            }
        }

        return matches;
    }

    /// <inheritdoc/>
    public IPackage GetMsixPackageInfo(string msixFilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(msixFilePath);
        return MsixPackage.Open(msixFilePath);
    }

    private IInstalledPackage? FindSingle(Func<IInstalledPackage, bool> predicate)
    {
        IInstalledPackage? found = null;
        foreach (IInstalledPackage package in _store.EnumeratePackages())
        {
            if (found is null && predicate(package))
            {
                found = package;
            }
            else
            {
                package.Dispose();
            }
        }

        return found;
    }
}
