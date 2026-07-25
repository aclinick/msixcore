using MsixCore.Packaging;
using MsixCore.Packaging.Integrity;

namespace MsixCore.Deployment;

/// <summary>
/// Default <see cref="IPackageManager"/> implementation.
/// </summary>
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
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageFilePath);
        var response = new MsixResponse(cancellationToken);
        _ = Task.Run(() => RunAdd(packageFilePath, options, response), CancellationToken.None);
        return response;
    }

    /// <inheritdoc/>
    public IMsixResponse RemovePackage(
        string packageFullName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageFullName);
        var response = new MsixResponse(cancellationToken);
        _ = Task.Run(() => RunRemove(packageFullName, response), CancellationToken.None);
        return response;
    }

    private void RunAdd(string packageFilePath, DeploymentOptions options, MsixResponse response)
    {
        string? staging = null;
        try
        {
            response.Report(InstallationStep.Started, 0f, "Starting installation.");
            response.Token.ThrowIfCancellationRequested();

            response.Report(InstallationStep.GetPackageInformation, 5f, "Reading package information.");

            // The package (and its underlying file handle) is fully released before commit/complete so
            // callers awaiting Completion never race with the still-open source file.
            using (MsixPackage package = MsixPackage.Open(packageFilePath))
            {
                response.Token.ThrowIfCancellationRequested();
                response.Report(InstallationStep.Extraction, 10f, "Extracting package payload.");
                staging = _store.CreateStagingLocation();
                var progress = new SynchronousProgress<float>(p =>
                    response.Report(InstallationStep.Extraction, 10f + (p * 0.85f), "Extracting package payload."));
                BlockMapVerificationResult verification = PackageExtractor.ExtractAndVerify(
                    package.Opc,
                    package.BlockMap,
                    staging,
                    progress,
                    response.Token);
                if (!verification.IsValid)
                {
                    throw new InvalidDataException(
                        "Package integrity check failed: the extracted payload does not match its block map.");
                }
            }

            InstalledPackageInfo packageInfo = InstalledPackageInfo.ReadFromDirectory(staging);

            // Re-check cancellation after the (uncancelable-in-the-final-chunk) copy so a cancel during
            // the last file does not still result in a committed installation.
            response.Token.ThrowIfCancellationRequested();

            response.Report(InstallationStep.Integration, 95f,
                options.HasFlag(DeploymentOptions.ExtractOnly)
                    ? "Skipping OS integration (ExtractOnly)."
                    : "Registering package.");

            // OS-integration handlers (shortcuts, associations, etc.) land in a later Windows phase.
            _store.Commit(staging, packageInfo, options);
            staging = null;

            response.Complete();
        }
        catch (Exception ex)
        {
            CleanupStaging(staging);
            response.Fail(ex);
        }
    }

    private void RunRemove(string packageFullName, MsixResponse response)
    {
        try
        {
            response.Report(InstallationStep.Started, 0f, "Starting removal.");
            response.Token.ThrowIfCancellationRequested();

            if (!_store.Contains(packageFullName))
            {
                throw new InvalidOperationException($"Package '{packageFullName}' is not installed.");
            }

            response.Report(InstallationStep.Extraction, 50f, "Removing package payload.");
            _store.Delete(packageFullName);

            response.Complete();
        }
        catch (Exception ex)
        {
            response.Fail(ex);
        }
    }

    private static void CleanupStaging(string? staging)
    {
        if (staging is not null && Directory.Exists(staging))
        {
            try
            {
                Directory.Delete(staging, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup of a failed install; never mask the original failure.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <inheritdoc/>
    public IInstalledPackage? FindPackage(string packageFullName)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageFullName);
        InstalledPackageInfo? info = _store.FindByFullName(packageFullName);
        return info is null ? null : InstalledPackage.FromInfo(info);
    }

    /// <inheritdoc/>
    public IInstalledPackage? FindPackageByFamilyName(string packageFamilyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageFamilyName);
        InstalledPackageInfo? info = _store.FindByFamilyName(packageFamilyName);
        return info is null ? null : InstalledPackage.FromInfo(info);
    }

    /// <inheritdoc/>
    public IReadOnlyList<IInstalledPackage> FindPackages(string searchParameter)
    {
        ArgumentException.ThrowIfNullOrEmpty(searchParameter);

        return _store.EnumeratePackages()
            .Where(package => Wildcard.IsMatch(searchParameter, package.Identity.PackageFullName))
            .Select(static package => (IInstalledPackage)InstalledPackage.FromInfo(package))
            .ToList();
    }

    /// <inheritdoc/>
    public IPackage GetMsixPackageInfo(string msixFilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(msixFilePath);
        return MsixPackage.Open(msixFilePath);
    }

}
