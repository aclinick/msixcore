using MsixCore.Packaging.Integrity;
using MsixCore.Packaging.Manifest;
using MsixCore.Packaging.Opc;
using MsixCore.Packaging.Validation;

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
    private static readonly HashSet<string> CachedSecurityParts = new(StringComparer.OrdinalIgnoreCase)
    {
        OpcPartNames.AppxManifest,
        OpcPartNames.AppxBlockMap,
        OpcPartNames.ContentTypes,
        OpcPartNames.CodeIntegrityCatalog,
        OpcPartNames.AppxSignature,
    };

    private readonly IOpcPackage _opc;
    private readonly IOpcPackage _securityCachingOpc;
    private readonly Lazy<AppxManifest> _manifest;
    private readonly Lazy<BlockMap> _blockMap;

    /// <summary>
    /// Cached raw bytes of security-relevant parts that must be parsed, hashed, copied, or reported
    /// from the <em>same</em> read. Payload files not listed in <see cref="CachedSecurityParts"/>
    /// deliberately remain streaming to keep memory independent of package size.
    /// </summary>
    private readonly Dictionary<string, byte[]> _securityPartCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Serializes access to <see cref="_securityPartCache"/> so that concurrent calls to
    /// <see cref="GetSecurityPartBytes"/> cannot both miss and both read the part (potentially
    /// reading different bytes at different times on a mutable directory).
    /// </summary>
    private readonly object _securityPartLock = new();
    private bool _disposed;

    private MsixPackage(IOpcPackage opc)
    {
        _opc = opc;
        _securityCachingOpc = new SecurityPartCachingOpcPackage(this);
        _manifest = new Lazy<AppxManifest>(ReadManifest);
        _blockMap = new Lazy<BlockMap>(ReadBlockMap);
    }

    /// <summary>The underlying OPC/ZIP container.</summary>
    public IOpcPackage Opc
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _securityCachingOpc;
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
        return BlockMapVerifier.Verify(_securityCachingOpc, BlockMap);
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

        byte[]? raw = GetSecurityPartBytes(OpcPartNames.AppxSignature);
        if (raw is null)
        {
            return null;
        }

        using var signature = new MemoryStream(raw, writable: false);
        return PackageSignatureReader.Read(signature);
    }

    /// <summary>
    /// Verifies that the signature's APPX indirect-data digest table binds this package's
    /// footprint parts (<c>[Content_Types].xml</c>, <c>AppxBlockMap.xml</c>, and optionally
    /// <c>AppxMetadata/CodeIntegrity.cat</c>) to the CMS signer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Container packages (<c>.msix</c>/<c>.appx</c>) opened from a file:</strong> Windows
    /// share modes block in-place writers while the handle is open. POSIX does not provide that
    /// guarantee, so cached security-part bytes provide internal consistency but not backing-file
    /// immutability.
    /// </para>
    /// <para>
    /// <strong>Container packages opened from a caller-supplied <see cref="Stream"/>:</strong>
    /// central-directory metadata is checked for drift and security-relevant parts are cached on
    /// first read. Same-length payload mutations, read races, and deceptive custom streams remain
    /// outside that check.
    /// </para>
    /// <para>
    /// <strong>Loose directory packages:</strong> validation is best-effort against a
    /// concurrently-writable directory. The implementation caches security-part bytes on first read
    /// and detects parts that appear after open, but a sufficiently-privileged attacker with
    /// write access to the directory <em>before</em> the package is opened can still present
    /// crafted content. For a hard security boundary, validate container files, not directories.
    /// </para>
    /// <para>
    /// Manifest, signature, block map, content-types, and code-integrity-catalog bytes all use one
    /// cache choke point. General payload remains streaming.
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

        // For directory-backed packages, detect any changes to the part set (additions,
        // removals) since open. This covers both footprint parts and payload parts.
        string? driftError = DetectDirectoryDrift();
        if (driftError is not null)
        {
            return new IndirectDataBindingResult
            {
                IsBindingValid = false,
                Results = [],
                Summary = $"APPX indirect-data binding FAILED — snapshot drift detected: {driftError}",
            };
        }

        // Build a snapshot dictionary from cached footprint bytes.
        var snapshots = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        SnapshotSecurityPart(OpcPartNames.ContentTypes, snapshots);
        SnapshotSecurityPart(OpcPartNames.AppxBlockMap, snapshots);
        SnapshotSecurityPart(OpcPartNames.CodeIntegrityCatalog, snapshots);

        return AppxDigestTableVerifier.VerifyFromSnapshots(signature.DigestTable, snapshots);
    }

    /// <summary>
    /// Asks the underlying <see cref="IOpcPackage"/> whether its current backing part set still
    /// matches the open-time snapshot. Returns the reported error or <see langword="null"/> when
    /// the implementation proves its snapshot remains consistent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The built-in directory implementation uses
    /// <see cref="DirectoryOpcPackage.EnumerateValidatedParts"/> — the single implementation
    /// shared with <see cref="DirectoryOpcPackage.Open"/> for traversal, normalization, validity,
    /// root-containment, and duplicate checks. Other implementations must fulfill the required
    /// <see cref="IOpcPackage.DetectSnapshotDrift"/> contract.
    /// </para>
    /// <para>
    /// This public method exposes the drift state for diagnostics. Block-map verification already
    /// enforces the same check through <see cref="BlockMapVerifier"/>.
    /// </para>
    /// </remarks>
    /// <returns>An error message describing the drift, or <see langword="null"/> if safe.</returns>
    public string? DetectDirectoryDrift()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _opc.DetectSnapshotDrift();
    }

    /// <summary>
    /// Ensures the named part's bytes are in <see cref="_securityPartCache"/>, reading from
    /// <see cref="_opc"/> only if not already cached. Returns the cached bytes. This is the
    /// single choke-point for all security-relevant part reads, so there is exactly one backing
    /// read per part per package lifetime. Serialized by <see cref="_securityPartLock"/> so
    /// concurrent callers cannot both miss and
    /// both read.
    /// </summary>
    /// <returns>The cached bytes, or <see langword="null"/> if the part does not exist.</returns>
    private byte[]? GetSecurityPartBytes(string partName)
    {
        string normalized = OpcPackage.NormalizeLookup(partName);
        lock (_securityPartLock)
        {
            if (_securityPartCache.TryGetValue(normalized, out byte[]? cached))
            {
                return cached;
            }

            if (!_opc.ContainsPart(normalized))
            {
                return null;
            }

            cached = ReadPartBytesUncached(normalized);
            _securityPartCache[normalized] = cached;
            return cached;
        }
    }

    /// <summary>
    /// Copies the cached bytes for <paramref name="partName"/> into <paramref name="snapshots"/>.
    /// Uses a defensive copy so the verifier cannot mutate the cache.
    /// </summary>
    private void SnapshotSecurityPart(string partName, Dictionary<string, byte[]> snapshots)
    {
        byte[]? cached = GetSecurityPartBytes(partName);
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

    /// <summary>
    /// Validates the manifest against the rules Windows enforces at deployment time: identifier
    /// form, package-type consistency, and version ranges. Parsing is deliberately tolerant, so this
    /// is opt-in.
    /// </summary>
    /// <returns>The issues found; see <see cref="ManifestValidationResult.IsValid"/>.</returns>
    /// <exception cref="InvalidDataException">The package has no manifest or it is malformed.</exception>
    /// <remarks>
    /// This validates the manifest only. Use <see cref="VerifyBlockMap"/> and
    /// <see cref="VerifySignatureBinding"/> for payload and signature integrity.
    /// </remarks>
    public ManifestValidationResult ValidateManifest()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        byte[]? raw = GetSecurityPartBytes(OpcPartNames.AppxManifest);
        if (raw is null)
        {
            throw MsixError.Format(MsixErrorCode.FootprintMissing,
                $"The package does not contain '{OpcPartNames.AppxManifest}'.");
        }

        using var manifest = new MemoryStream(raw, writable: false);
        return ManifestValidator.Validate(manifest);
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
        byte[]? raw = GetSecurityPartBytes(OpcPartNames.AppxManifest);
        if (raw is null)
        {
            throw MsixError.Format(MsixErrorCode.FootprintMissing,
                $"The package does not contain '{OpcPartNames.AppxManifest}'.");
        }

        using var manifest = new MemoryStream(raw, writable: false);
        return AppxManifestParser.Parse(manifest);
    }

    private BlockMap ReadBlockMap()
    {
        // Go through the single choke-point so the raw bytes are cached and shared with
        // binding verification — guaranteeing single-read-single-hash across the pipeline.
        byte[]? raw = GetSecurityPartBytes(OpcPartNames.AppxBlockMap);
        if (raw is null)
        {
            throw MsixError.Format(MsixErrorCode.FootprintMissing,
                $"The package does not contain '{OpcPartNames.AppxBlockMap}'.");
        }

        using var blockMap = new MemoryStream(raw, writable: false);
        return BlockMapParser.Parse(blockMap);
    }

    private sealed class SecurityPartCachingOpcPackage(MsixPackage owner) : IOpcPackage
    {
        public IReadOnlyCollection<string> PartNames => owner._opc.PartNames;

        public string? DetectSnapshotDrift() => owner._opc.DetectSnapshotDrift();

        public OpcPartZipInfo? GetZipInfo(string partName) => owner._opc.GetZipInfo(partName);

        public bool ContainsPart(string partName) => owner._opc.ContainsPart(partName);

        public Stream OpenPart(string partName)
        {
            string normalized = OpcPackage.NormalizeLookup(partName);
            if (!CachedSecurityParts.Contains(normalized))
            {
                return owner._opc.OpenPart(partName);
            }

            byte[]? cached = owner.GetSecurityPartBytes(normalized);
            if (cached is null)
            {
                throw new FileNotFoundException($"Part '{partName}' was not found in the package.", partName);
            }

            return new MemoryStream(cached, writable: false);
        }

        public void Dispose()
        {
            owner._opc.Dispose();
        }
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
