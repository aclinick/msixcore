using MsixCore.Packaging;
using MsixCore.Packaging.Integrity;

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
            using MsixPackage package = MsixPackage.Open(packageFilePath);
            string fullName = package.Identity.PackageFullName;

            BlockMapVerificationResult verification = package.VerifyBlockMap();
            if (!verification.IsValid)
            {
                throw new InvalidDataException(
                    $"Package integrity check failed for '{fullName}': the payload does not match its block map.");
            }

            if (_store.Contains(fullName) && !options.HasFlag(DeploymentOptions.ForceApplicationShutdown))
            {
                throw new InvalidOperationException(
                    $"Package '{fullName}' is already installed. Use ForceApplicationShutdown to reinstall.");
            }

            response.Token.ThrowIfCancellationRequested();
            response.Report(InstallationStep.Extraction, 10f, "Extracting package payload.");
            staging = _store.CreateStagingLocation();
            var progress = new Progress<float>(p =>
                response.Report(InstallationStep.Extraction, 10f + (p * 0.85f), "Extracting package payload."));
            PackageExtractor.Extract(package.Opc, staging, progress, response.Token);

            response.Report(InstallationStep.Integration, 95f,
                options.HasFlag(DeploymentOptions.ExtractOnly)
                    ? "Skipping OS integration (ExtractOnly)."
                    : "Registering package.");

            // OS-integration handlers (shortcuts, associations, etc.) land in a later Windows phase.
            _store.Commit(staging, fullName);
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
        IReadOnlyList<IInstalledPackage> candidates = _store.EnumeratePackages();
        int index = 0;
        try
        {
            for (; index < candidates.Count; index++)
            {
                IInstalledPackage package = candidates[index];
                if (Wildcard.IsMatch(searchParameter, package.Identity.PackageFullName))
                {
                    matches.Add(package);
                }
                else
                {
                    package.Dispose();
                }
            }
        }
        catch
        {
            // Dispose everything not handed back to the caller: matches accumulated so far and any
            // remaining candidates we never examined.
            DisposeAll(matches);
            for (int i = index; i < candidates.Count; i++)
            {
                Dispose(candidates[i]);
            }

            throw;
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
        IReadOnlyList<IInstalledPackage> candidates = _store.EnumeratePackages();
        int index = 0;
        try
        {
            for (; index < candidates.Count; index++)
            {
                IInstalledPackage package = candidates[index];
                if (found is null && predicate(package))
                {
                    found = package;
                }
                else
                {
                    package.Dispose();
                }
            }
        }
        catch
        {
            found?.Dispose();
            for (int i = index; i < candidates.Count; i++)
            {
                Dispose(candidates[i]);
            }

            throw;
        }

        return found;
    }

    private static void DisposeAll(IEnumerable<IInstalledPackage> packages)
    {
        foreach (IInstalledPackage package in packages)
        {
            Dispose(package);
        }
    }

    private static void Dispose(IInstalledPackage package)
    {
        try
        {
            package.Dispose();
        }
        catch
        {
            // Best-effort cleanup: never mask the original failure.
        }
    }
}
