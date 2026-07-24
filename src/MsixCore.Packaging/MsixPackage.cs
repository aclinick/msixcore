using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging;

/// <summary>
/// The primary entry point for reading an MSIX/APPX package from disk or a stream.
/// </summary>
/// <remarks>
/// Phase 1 opens the underlying OPC/ZIP container (exposed via <see cref="Opc"/>). Manifest-derived
/// members (<see cref="Identity"/>, <see cref="DisplayName"/>, etc.) are implemented in Phase 2.
/// </remarks>
public sealed class MsixPackage : IPackage
{
    private readonly OpcPackage _opc;
    private bool _disposed;

    private MsixPackage(OpcPackage opc) => _opc = opc;

    /// <summary>The underlying OPC/ZIP container.</summary>
    public IOpcPackage Opc
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _opc;
        }
    }

    /// <inheritdoc/>
    public PackageIdentity Identity =>
        throw new NotImplementedException("Implemented in Phase 2 (manifest parsing).");

    /// <inheritdoc/>
    public string DisplayName =>
        throw new NotImplementedException("Implemented in Phase 2 (manifest parsing).");

    /// <inheritdoc/>
    public string PublisherDisplayName =>
        throw new NotImplementedException("Implemented in Phase 2 (manifest parsing).");

    /// <inheritdoc/>
    public IReadOnlyList<string> Capabilities =>
        throw new NotImplementedException("Implemented in Phase 2 (manifest parsing).");

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
    public Stream? OpenLogo() =>
        throw new NotImplementedException("Implemented in Phase 2 (manifest parsing).");

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
}
