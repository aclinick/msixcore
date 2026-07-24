using MsixCore.Packaging;

namespace MsixCore.Deployment;

/// <summary>
/// Manages the lifecycle of MSIX packages on the local machine: add (install), remove (uninstall),
/// and query. The C# analogue of the native <c>IPackageManager</c>, reshaped to use
/// <see cref="Task"/>-based async and exceptions instead of <c>HRESULT</c>.
/// </summary>
public interface IPackageManager
{
    /// <summary>Adds (installs) a package from a file path.</summary>
    /// <param name="packageFilePath">Path to the <c>.msix</c>/<c>.appx</c> package.</param>
    /// <param name="options">Deployment options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A response handle, returned immediately, whose <see cref="IMsixResponse.Completion"/> task
    /// completes when the operation finishes. Progress is observable via
    /// <see cref="IMsixResponse.ProgressChanged"/> and the operation can be cancelled via
    /// <see cref="IMsixResponse.Cancel"/> while it runs.
    /// </returns>
    IMsixResponse AddPackage(
        string packageFilePath,
        DeploymentOptions options = DeploymentOptions.None,
        CancellationToken cancellationToken = default);

    /// <summary>Removes (uninstalls) a package by its full name.</summary>
    /// <param name="packageFullName">The package full name to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A response handle, returned immediately, whose <see cref="IMsixResponse.Completion"/> task
    /// completes when the operation finishes.
    /// </returns>
    IMsixResponse RemovePackage(
        string packageFullName,
        CancellationToken cancellationToken = default);

    /// <summary>Finds a single installed package by its full name.</summary>
    /// <returns>The installed package, or <see langword="null"/> if not found.</returns>
    IInstalledPackage? FindPackage(string packageFullName);

    /// <summary>Finds a single installed package by its family name.</summary>
    /// <returns>The installed package, or <see langword="null"/> if not found.</returns>
    IInstalledPackage? FindPackageByFamilyName(string packageFamilyName);

    /// <summary>
    /// Finds installed packages matching a search parameter, which may contain the wildcards
    /// <c>*</c> (any run of characters) and <c>?</c> (single character).
    /// </summary>
    IReadOnlyList<IInstalledPackage> FindPackages(string searchParameter);

    /// <summary>Reads package metadata from an MSIX file without installing it.</summary>
    /// <param name="msixFilePath">Path to the <c>.msix</c>/<c>.appx</c> package.</param>
    IPackage GetMsixPackageInfo(string msixFilePath);
}
