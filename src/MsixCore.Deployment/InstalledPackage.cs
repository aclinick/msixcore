using MsixCore.Packaging;
using MsixCore.Packaging.Manifest;

namespace MsixCore.Deployment;

/// <summary>
/// An <see cref="IInstalledPackage"/> backed by an unpacked ("loose") package layout on disk, as
/// produced by the deployment engine and tracked by an <see cref="IPackageStore"/>.
/// </summary>
public sealed class InstalledPackage : IInstalledPackage
{
    private readonly MsixPackage _package;
    private readonly Lazy<ExecutionInfo?> _executionInfo;
    private bool _disposed;

    private InstalledPackage(MsixPackage package, string installedLocation)
    {
        _package = package;
        InstalledLocation = installedLocation;
        _executionInfo = new Lazy<ExecutionInfo?>(ResolveExecutionInfo);
    }

    /// <inheritdoc/>
    public string InstalledLocation { get; }

    /// <inheritdoc/>
    public PackageIdentity Identity => _package.Identity;

    /// <inheritdoc/>
    public string DisplayName => _package.DisplayName;

    /// <inheritdoc/>
    public string PublisherDisplayName => _package.PublisherDisplayName;

    /// <inheritdoc/>
    public IReadOnlyList<string> Capabilities => _package.Capabilities;

    /// <inheritdoc/>
    public ExecutionInfo? ExecutionInfo => _executionInfo.Value;

    /// <summary>Opens the installed package from its unpacked directory.</summary>
    /// <param name="directory">The directory containing the unpacked package.</param>
    /// <returns>An open <see cref="InstalledPackage"/>.</returns>
    public static InstalledPackage OpenDirectory(string directory)
    {
        MsixPackage package = MsixPackage.OpenDirectory(directory);
        return new InstalledPackage(package, package.Opc is Packaging.Opc.DirectoryOpcPackage dir ? dir.RootDirectory : directory);
    }

    /// <inheritdoc/>
    public Stream? OpenLogo() => _package.OpenLogo();

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _package.Dispose();
    }

    private ExecutionInfo? ResolveExecutionInfo()
    {
        ManifestApplication? app = _package.Manifest.Applications
            .FirstOrDefault(a => !string.IsNullOrEmpty(a.Executable));
        if (app?.Executable is not { Length: > 0 } executable)
        {
            return null;
        }

        string relative = executable.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        return new ExecutionInfo
        {
            ResolvedExecutableFilePath = Path.Combine(InstalledLocation, relative),
            WorkingDirectory = InstalledLocation,
        };
    }
}
