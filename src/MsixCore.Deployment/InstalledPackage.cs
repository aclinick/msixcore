using MsixCore.Packaging;
namespace MsixCore.Deployment;

/// <summary>
/// An <see cref="IInstalledPackage"/> backed by an unpacked ("loose") package layout on disk, as
/// produced by the deployment engine and tracked by an <see cref="IPackageStore"/>.
/// </summary>
public sealed class InstalledPackage : IInstalledPackage
{
    private readonly InstalledPackageInfo _info;
    private readonly Lazy<MsixPackage> _package;
    private readonly Lazy<ExecutionInfo?> _executionInfo;
    private bool _disposed;

    private InstalledPackage(InstalledPackageInfo info)
    {
        _info = info;
        _package = new Lazy<MsixPackage>(info.OpenPackage);
        _executionInfo = new Lazy<ExecutionInfo?>(ResolveExecutionInfo);
    }

    /// <inheritdoc/>
    public string InstalledLocation
    {
        get
        {
            ThrowIfDisposed();
            return _info.InstalledLocation;
        }
    }

    /// <inheritdoc/>
    public PackageIdentity Identity
    {
        get
        {
            ThrowIfDisposed();
            return _info.Identity;
        }
    }

    /// <inheritdoc/>
    public string DisplayName
    {
        get
        {
            ThrowIfDisposed();
            return _info.DisplayName;
        }
    }

    /// <inheritdoc/>
    public string PublisherDisplayName
    {
        get
        {
            ThrowIfDisposed();
            return _info.PublisherDisplayName;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Capabilities
    {
        get
        {
            ThrowIfDisposed();
            return _info.Capabilities;
        }
    }

    /// <inheritdoc/>
    public ExecutionInfo? ExecutionInfo
    {
        get
        {
            ThrowIfDisposed();
            return _executionInfo.Value;
        }
    }

    /// <summary>Opens the installed package from its unpacked directory.</summary>
    /// <param name="directory">The directory containing the unpacked package.</param>
    /// <returns>An open <see cref="InstalledPackage"/>.</returns>
    public static InstalledPackage OpenDirectory(string directory)
    {
        return new InstalledPackage(InstalledPackageInfo.ReadFromDirectory(directory));
    }

    internal static InstalledPackage FromInfo(InstalledPackageInfo info) => new(info);

    /// <inheritdoc/>
    public Stream? OpenLogo()
    {
        ThrowIfDisposed();
        return _package.Value.OpenLogo();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_package.IsValueCreated)
        {
            _package.Value.Dispose();
        }
    }

    private ExecutionInfo? ResolveExecutionInfo()
    {
        if (_info.ExecutablePath is not { Length: > 0 } executable)
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

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
