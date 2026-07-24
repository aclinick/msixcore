using MsixCore.Packaging.Manifest;
using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging;

/// <summary>
/// The primary entry point for reading an MSIX/APPX package from disk or a stream.
/// </summary>
/// <remarks>
/// The underlying OPC/ZIP container is exposed via <see cref="Opc"/>; the parsed
/// <c>AppxManifest.xml</c> is exposed via <see cref="Manifest"/> and read lazily on first access.
/// </remarks>
public sealed class MsixPackage : IPackage
{
    private readonly OpcPackage _opc;
    private readonly Lazy<AppxManifest> _manifest;
    private bool _disposed;

    private MsixPackage(OpcPackage opc)
    {
        _opc = opc;
        _manifest = new Lazy<AppxManifest>(ReadManifest);
    }

    /// <summary>The underlying OPC/ZIP container.</summary>
    public IOpcPackage Opc
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _opc;
        }
    }

    /// <summary>The parsed <c>AppxManifest.xml</c>.</summary>
    /// <exception cref="InvalidDataException">The package has no manifest or it is malformed.</exception>
    public AppxManifest Manifest
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _manifest.Value;
        }
    }

    /// <inheritdoc/>
    public PackageIdentity Identity => Manifest.Identity;

    /// <inheritdoc/>
    public string DisplayName => Manifest.DisplayName;

    /// <inheritdoc/>
    public string PublisherDisplayName => Manifest.PublisherDisplayName;

    /// <inheritdoc/>
    public IReadOnlyList<string> Capabilities => Manifest.Capabilities;

    /// <summary>Opens an MSIX/APPX package from a file path.</summary>
    /// <param name="path">Path to a <c>.msix</c>/<c>.appx</c> (or bundle) file.</param>
    /// <returns>An open <see cref="MsixPackage"/>.</returns>
    public static MsixPackage Open(string path) => new(OpcPackage.Open(path));

    /// <summary>Opens an MSIX/APPX package from a seekable stream.</summary>
    /// <param name="stream">A readable, seekable stream positioned at the start of the package.</param>
    /// <param name="leaveOpen">Whether to leave <paramref name="stream"/> open when this package is disposed.</param>
    /// <returns>An open <see cref="MsixPackage"/>.</returns>
    public static MsixPackage Open(Stream stream, bool leaveOpen = false) =>
        new(OpcPackage.Open(stream, leaveOpen));

    /// <inheritdoc/>
    public Stream? OpenLogo()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        string? logo = Manifest.Logo;
        if (string.IsNullOrEmpty(logo) || !_opc.ContainsPart(logo))
        {
            return null;
        }

        return _opc.OpenPart(logo);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _opc.Dispose();
    }

    private AppxManifest ReadManifest()
    {
        if (!_opc.ContainsPart(OpcPartNames.AppxManifest))
        {
            throw new InvalidDataException(
                $"The package does not contain '{OpcPartNames.AppxManifest}'.");
        }

        using Stream manifestStream = _opc.OpenPart(OpcPartNames.AppxManifest);
        return AppxManifestParser.Parse(manifestStream);
    }
}
