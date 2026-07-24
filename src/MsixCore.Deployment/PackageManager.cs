using MsixCore.Packaging;

namespace MsixCore.Deployment;

/// <summary>
/// Default <see cref="IPackageManager"/> implementation.
/// </summary>
/// <remarks>
/// Phase 0 provides the type and public surface. Real behavior lands in Phase 4 (query surface)
/// and Phase 5 (extraction pipeline); Windows OS-integration handlers land in Phase 6.
/// </remarks>
public sealed class PackageManager : IPackageManager
{
    /// <inheritdoc/>
    public Task<IMsixResponse> AddPackageAsync(
        string packageFilePath,
        DeploymentOptions options = DeploymentOptions.None,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Implemented in Phase 5 (deployment engine).");

    /// <inheritdoc/>
    public Task<IMsixResponse> RemovePackageAsync(
        string packageFullName,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Implemented in Phase 5 (deployment engine).");

    /// <inheritdoc/>
    public IInstalledPackage? FindPackage(string packageFullName) =>
        throw new NotImplementedException("Implemented in Phase 4 (query surface).");

    /// <inheritdoc/>
    public IInstalledPackage? FindPackageByFamilyName(string packageFamilyName) =>
        throw new NotImplementedException("Implemented in Phase 4 (query surface).");

    /// <inheritdoc/>
    public IReadOnlyList<IInstalledPackage> FindPackages(string searchParameter) =>
        throw new NotImplementedException("Implemented in Phase 4 (query surface).");

    /// <inheritdoc/>
    public IPackage GetMsixPackageInfo(string msixFilePath) =>
        throw new NotImplementedException("Implemented in Phase 4 (query surface).");
}
