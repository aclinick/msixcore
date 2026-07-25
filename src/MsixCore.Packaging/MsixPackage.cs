using MsixCore.Packaging.Integrity;
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
    private readonly IOpcPackage _opc;
    private readonly Lazy<AppxManifest> _manifest;
    private readonly Lazy<BlockMap> _blockMap;

    /// <summary>
    /// Cached raw bytes of footprint parts that are read during parsing and must be hashed
    /// at binding time over the <em>same</em> bytes. Eliminates TOCTOU exposure on
    /// directory-backed packages where <see cref="IOpcPackage.OpenPart"/> re-opens the live
    /// file on each call.
    /// </summary>
    private readonly Dictionary<string, byte[]> _footprintCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Serializes access to <see cref="_footprintCache"/> so that concurrent calls to
    /// <see cref="GetFootprintBytes"/> cannot both miss and both read the file (potentially
    /// reading different bytes at different times on a mutable directory).
    /// </summary>
    private readonly object _footprintLock = new();
    private bool _disposed;

    private MsixPackage(IOpcPackage opc)
    {
        _opc = opc;
        _manifest = new Lazy<AppxManifest>(ReadManifest);
        _blockMap = new Lazy<BlockMap>(ReadBlockMap);
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

    /// <summary>The parsed <c>AppxBlockMap.xml</c>, read lazily on first access.</summary>
    /// <exception cref="InvalidDataException">The package has no block map or it is malformed.</exception>
    public BlockMap BlockMap
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _blockMap.Value;
        }
    }

    /// <summary>Whether the package carries an <c>AppxSignature.p7x</c> part.</summary>
    public bool IsSigned
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _opc.ContainsPart(OpcPartNames.AppxSignature);
        }
    }

    /// <summary>Opens an MSIX/APPX package from a file path.</summary>
    /// <param name="path">Path to a <c>.msix</c>/<c>.appx</c> file.</param>
    /// <returns>An open <see cref="MsixPackage"/>.</returns>
    /// <exception cref="MsixPackageTypeException">
    /// The container is a bundle and must be opened with <see cref="MsixBundle.Open(string)"/>.
    /// </exception>
    public static MsixPackage Open(string path) => Create(OpcPackage.Open(path));

    /// <summary>Opens an MSIX/APPX package from a seekable stream.</summary>
    /// <param name="stream">A readable, seekable stream positioned at the start of the package.</param>
    /// <param name="leaveOpen">Whether to leave <paramref name="stream"/> open when this package is disposed.</param>
    /// <returns>An open <see cref="MsixPackage"/>.</returns>
    /// <exception cref="MsixPackageTypeException">
    /// The container is a bundle and must be opened with <see cref="MsixBundle.Open(Stream, bool)"/>.
    /// </exception>
    public static MsixPackage Open(Stream stream, bool leaveOpen = false) =>
        Create(OpcPackage.Open(stream, leaveOpen));

    /// <summary>Opens a package from an unpacked ("loose") layout on disk.</summary>
    /// <param name="directory">A directory containing the unpacked package (with <c>AppxManifest.xml</c>).</param>
    /// <returns>An open <see cref="MsixPackage"/> over the loose layout.</returns>
    public static MsixPackage OpenDirectory(string directory) => new(DirectoryOpcPackage.Open(directory));

    /// <summary>Returns whether the container at <paramref name="path"/> is an MSIX/APPX bundle.</summary>
    public static bool IsBundle(string path)
    {
        using OpcPackage opc = OpcPackage.Open(path);
        return opc.ContainsPart(OpcPartNames.AppxBundleManifest);
    }

    /// <summary>
    /// Verifies the package payload against its block map: every block-mapped file's content hashes
    /// and total size match, and the block map and package payload cover the same files.
    /// </summary>
    /// <returns>The verification result.</returns>
    /// <exception cref="InvalidDataException">The block map is missing or malformed.</exception>
    public BlockMapVerificationResult VerifyBlockMap()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return BlockMapVerifier.Verify(_opc, BlockMap);
    }

    /// <summary>
    /// Reads the signer identity and CMS integrity status from <c>AppxSignature.p7x</c>, or
    /// <see langword="null"/> if the package is unsigned.
    /// </summary>
    /// <returns>The signature information, or <see langword="null"/> for an unsigned package.</returns>
    /// <exception cref="InvalidDataException">The signature part is present but malformed.</exception>
    public PackageSignature? ReadSignature()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_opc.ContainsPart(OpcPartNames.AppxSignature))
        {
            return null;
        }

        using Stream signature = _opc.OpenPart(OpcPartNames.AppxSignature);
        return PackageSignatureReader.Read(signature);
    }

    /// <summary>
    /// Verifies that the signature's APPX indirect-data digest table binds this package's
    /// footprint parts (<c>[Content_Types].xml</c>, <c>AppxBlockMap.xml</c>, and optionally
    /// <c>AppxMetadata/CodeIntegrity.cat</c>) to the CMS signer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Container packages (<c>.msix</c>/<c>.appx</c>):</strong> the underlying ZIP
    /// archive is opened once from a single stream. The single-read-single-hash guarantee is
    /// unconditional — no concurrent modification is possible.
    /// </para>
    /// <para>
    /// <strong>Loose directory packages:</strong> validation is best-effort against a
    /// concurrently-writable directory. The implementation caches footprint bytes on first read
    /// and detects parts that appear after open, but a sufficiently-privileged attacker with
    /// write access to the directory <em>before</em> the package is opened can still present
    /// crafted content. For a hard security boundary, validate container files, not directories.
    /// </para>
    /// <para>
    /// For <c>AppxBlockMap.xml</c>, binding hashes the exact bytes that were read and parsed
    /// by the block-map parser. <c>[Content_Types].xml</c> and <c>AppxMetadata/CodeIntegrity.cat</c>
    /// are read once at binding time and cached.
    /// </para>
    /// </remarks>
    /// <param name="signature">
    /// The signature previously obtained from <see cref="ReadSignature"/>. Must have a valid CMS
    /// envelope and a parsed <see cref="PackageSignature.DigestTable"/>.
    /// </param>
    /// <returns>A structured result describing which digests were verified and their status.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="signature"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The CMS envelope is invalid or the digest table could not be parsed.
    /// </exception>
    public IndirectDataBindingResult VerifySignatureBinding(PackageSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!signature.IsCmsIntegrityValid)
        {
            throw new InvalidOperationException(
                "Cannot verify signature binding: the CMS envelope integrity check failed.");
        }

        if (signature.DigestTable is null)
        {
            throw new InvalidOperationException(
                $"Cannot verify signature binding: {signature.DigestTableError ?? "the digest table is not available."}");
        }

        // For directory-backed packages, detect footprint parts that appeared on disk after
        // the package was opened. The open-time snapshot is frozen, so ContainsPart would
        // return false for newly-created files — an attacker could slip an unsigned
        // CodeIntegrity.cat after open and we would never notice. Fail closed on drift.
        string? driftError = DetectFootprintDrift();
        if (driftError is not null)
        {
            return new IndirectDataBindingResult
            {
                IsBindingValid = false,
                Results = [new DigestEntryResult
                {
                    Tag = AppxDigestTag.Axci,
                    Status = DigestVerificationStatus.DigestMissing,
                    Detail = driftError,
                }],
                Summary = $"APPX indirect-data binding FAILED — {driftError}",
            };
        }

        // Build a snapshot dictionary from cached footprint bytes.
        var snapshots = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        SnapshotFootprint(OpcPartNames.ContentTypes, snapshots);
        SnapshotFootprint(OpcPartNames.AppxBlockMap, snapshots);
        SnapshotFootprint(OpcPartNames.CodeIntegrityCatalog, snapshots);

        return AppxDigestTableVerifier.VerifyFromSnapshots(signature.DigestTable, snapshots);
    }

    /// <summary>
    /// For <see cref="DirectoryOpcPackage"/>-backed packages, checks whether any footprint
    /// part that was absent at open time now exists on the live filesystem. Returns an error
    /// message if drift is detected, or <see langword="null"/> if safe.
    /// </summary>
    /// <remarks>
    /// Container-backed packages (<c>.msix</c>/<c>.appx</c> files) are inherently safe — the
    /// ZIP archive is a single immutable stream. This check only applies to loose directories.
    /// </remarks>
    private string? DetectFootprintDrift()
    {
        if (_opc is not DirectoryOpcPackage dirPkg)
        {
            return null; // Container packages: no drift possible.
        }

        // Footprint parts that binding verification cares about.
        ReadOnlySpan<string> footprintParts =
        [
            OpcPartNames.ContentTypes,
            OpcPartNames.AppxBlockMap,
            OpcPartNames.CodeIntegrityCatalog,
        ];

        string root = dirPkg.RootDirectory;
        foreach (string partName in footprintParts)
        {
            if (_opc.ContainsPart(partName))
            {
                continue; // Was present at open time — already in the snapshot, fine.
            }

            // Check if the file now exists on the live filesystem.
            string fullPath = Path.Combine(root, partName.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                return $"Footprint part '{partName}' was absent when the package was opened but now exists on disk — the directory has been modified. Binding cannot be trusted.";
            }
        }

        return null;
    }

    /// <summary>
    /// Ensures the named part's bytes are in <see cref="_footprintCache"/>, reading from
    /// <see cref="_opc"/> only if not already cached. Returns the cached bytes. This is the
    /// single choke-point for all footprint-part reads — every method that needs raw footprint
    /// bytes must go through here so there is exactly one read per part per package lifetime.
    /// Serialized by <see cref="_footprintLock"/> so concurrent callers cannot both miss and
    /// both read.
    /// </summary>
    /// <returns>The cached bytes, or <see langword="null"/> if the part does not exist.</returns>
    private byte[]? GetFootprintBytes(string partName)
    {
        lock (_footprintLock)
        {
            if (_footprintCache.TryGetValue(partName, out byte[]? cached))
            {
                return cached;
            }

            if (!_opc.ContainsPart(partName))
            {
                return null;
            }

            cached = ReadPartBytesUncached(partName);
            _footprintCache[partName] = cached;
            return cached;
        }
    }

    /// <summary>
    /// Copies the cached bytes for <paramref name="partName"/> into <paramref name="snapshots"/>.
    /// Uses a defensive copy so the verifier cannot mutate the cache.
    /// </summary>
    private void SnapshotFootprint(string partName, Dictionary<string, byte[]> snapshots)
    {
        byte[]? cached = GetFootprintBytes(partName);
        if (cached is null)
        {
            return; // Part does not exist — binding verifier handles absence.
        }

        byte[] copy = new byte[cached.Length];
        cached.AsSpan().CopyTo(copy);
        snapshots[partName] = copy;
    }

    private byte[] ReadPartBytesUncached(string partName)
    {
        using Stream stream = _opc.OpenPart(partName);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

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

    private BlockMap ReadBlockMap()
    {
        // Go through the single choke-point so the raw bytes are cached and shared with
        // binding verification — guaranteeing single-read-single-hash across the pipeline.
        byte[]? raw = GetFootprintBytes(OpcPartNames.AppxBlockMap);
        if (raw is null)
        {
            throw new InvalidDataException(
                $"The package does not contain '{OpcPartNames.AppxBlockMap}'.");
        }

        return BlockMapParser.Parse(new MemoryStream(raw, writable: false));
    }

    private static MsixPackage Create(OpcPackage opc)
    {
        if (opc.ContainsPart(OpcPartNames.AppxBundleManifest))
        {
            opc.Dispose();
            throw new MsixPackageTypeException(
                "The container is an MSIX bundle. Open it with MsixBundle.Open instead of MsixPackage.Open.");
        }

        return new MsixPackage(opc);
    }
}
