using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging;

/// <summary>
/// The primary entry point for reading an MSIX/APPX package from disk or a stream.
/// </summary>
/// <remarks>
/// Phase 0 defines the public surface only; the concrete reader (OPC container, manifest binding,
/// identity computation) is implemented in Phase 1 and Phase 2.
/// </remarks>
public sealed class MsixPackage : IPackage
{
    private MsixPackage()
    {
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
    public static MsixPackage Open(string path) =>
        throw new NotImplementedException("Implemented in Phase 1 (OPC reader).");

    /// <summary>Opens an MSIX/APPX package from a seekable stream.</summary>
    /// <param name="stream">A readable, seekable stream positioned at the start of the package.</param>
    /// <param name="leaveOpen">Whether to leave <paramref name="stream"/> open when this package is disposed.</param>
    /// <returns>An open <see cref="MsixPackage"/>.</returns>
    public static MsixPackage Open(Stream stream, bool leaveOpen = false) =>
        throw new NotImplementedException("Implemented in Phase 1 (OPC reader).");

    /// <inheritdoc/>
    public Stream? OpenLogo() =>
        throw new NotImplementedException("Implemented in Phase 2 (manifest parsing).");

    /// <inheritdoc/>
    public void Dispose()
    {
        // No unmanaged/owned resources yet; real disposal lands with the Phase 1 OPC reader.
    }
}
