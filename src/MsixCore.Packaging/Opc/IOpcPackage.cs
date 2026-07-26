namespace MsixCore.Packaging.Opc;

/// <summary>
/// Low-level, cross-platform reader for the Open Packaging Conventions (OPC) ZIP container that
/// backs every MSIX/APPX package. Implemented in Phase 1 on top of
/// <see cref="System.IO.Compression.ZipArchive"/>.
/// </summary>
public interface IOpcPackage : IDisposable
{
    /// <summary>The logical part names (forward-slash, package-root-relative) contained in the package.</summary>
    IReadOnlyCollection<string> PartNames { get; }

    /// <summary>
    /// Returns an error when the current backing part set no longer matches <see cref="PartNames"/>,
    /// or <see langword="null"/> when the implementation can prove that snapshot is still consistent.
    /// Implementations backed by mutable stores must perform the required consistency check here.
    /// </summary>
    string? DetectSnapshotDrift();

    /// <summary>
    /// Returns ZIP metadata for a part, or <see langword="null"/> when the package has no ZIP
    /// container (for example, a loose directory package) or its backing stream cannot expose a
    /// stable ZIP snapshot. The latter condition must also be reported by
    /// <see cref="DetectSnapshotDrift"/> so verification fails closed.
    /// </summary>
    /// <remarks>
    /// This is a required capability so integrity verification can enforce block-map compressed
    /// sizes without depending on a concrete <see cref="IOpcPackage"/> implementation.
    /// </remarks>
    OpcPartZipInfo? GetZipInfo(string partName);

    /// <summary>Returns <see langword="true"/> if a part with the given name exists (case-insensitive).</summary>
    /// <param name="partName">Package-root-relative part name, e.g. <c>AppxManifest.xml</c>.</param>
    bool ContainsPart(string partName);

    /// <summary>Opens the named part for reading.</summary>
    /// <param name="partName">Package-root-relative part name.</param>
    /// <returns>A readable stream the caller must dispose.</returns>
    /// <exception cref="System.IO.FileNotFoundException">The part does not exist.</exception>
    Stream OpenPart(string partName);
}

/// <summary>ZIP sizes and compression mode for one canonical OPC part.</summary>
public sealed record OpcPartZipInfo(long UncompressedSize, long CompressedSize, bool IsCompressed);
