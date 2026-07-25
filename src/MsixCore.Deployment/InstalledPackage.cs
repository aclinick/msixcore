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

        // A manifest is untrusted input: reject rooted paths or ".." escapes that would resolve the
        // executable outside the install location.
        if (Path.IsPathRooted(relative) || IsWindowsDrivePath(relative))
        {
            return null;
        }

        string root = Path.GetFullPath(InstalledLocation);
        string resolved = Path.GetFullPath(Path.Combine(root, relative));
        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            return null;
        }

        return new ExecutionInfo
        {
            ResolvedExecutableFilePath = resolved,
            WorkingDirectory = root,
        };
    }

    private static bool IsWindowsDrivePath(string path) =>
        path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';
}
