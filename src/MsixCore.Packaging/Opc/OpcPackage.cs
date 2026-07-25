using System.IO.Compression;

namespace MsixCore.Packaging.Opc;

/// <summary>
/// Cross-platform <see cref="IOpcPackage"/> implementation backed by
/// <see cref="System.IO.Compression.ZipArchive"/>. Every MSIX/APPX package (and bundle) is an OPC
/// ZIP container, so this is the foundation the rest of the reader builds on.
/// </summary>
/// <remarks>
/// Instances are read-only but not thread-safe: concurrent calls to <see cref="OpenPart(string)"/>
/// share the underlying <see cref="ZipArchive"/> and must be externally synchronized.
/// </remarks>
public sealed class OpcPackage : IOpcPackage
{
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entriesByPart;
    private readonly List<string> _partNames;

    private OpcPackage(ZipArchive archive)
    {
        _archive = archive;
        _entriesByPart = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        _partNames = new List<string>(archive.Entries.Count);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            // Skip directory entries (zip directory markers end with '/').
            if (entry.FullName.EndsWith('/'))
            {
                continue;
            }

            if (!IsValidPartName(entry.FullName))
            {
                throw new InvalidDataException(
                    $"The package contains an invalid OPC part name: '{entry.FullName}'.");
            }

            // OPC part names are percent-encoded in the ZIP (e.g. '!' -> '%21'), but the block map,
            // manifest, and file system all use the decoded logical name. Canonicalize to the decoded
            // form so lookups and coverage checks line up. Decode each '/'-delimited segment on its own
            // and reject an encoded separator ('%2f'/'%5c') so it can't smuggle in a new path boundary,
            // then re-validate because decoding can reintroduce traversal segments (e.g. '%2e%2e' -> '..').
            if (!TryCanonicalizePartName(entry.FullName, out string partName) || !IsValidPartName(partName))
            {
                throw new InvalidDataException(
                    $"The package contains an invalid OPC part name: '{entry.FullName}'.");
            }

            // OPC forbids equivalent (including case-insensitively equal) part names.
            if (!_entriesByPart.TryAdd(partName, entry))
            {
                throw new InvalidDataException(
                    $"The package contains a duplicate OPC part name: '{partName}'.");
            }

            _partNames.Add(partName);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> PartNames => _partNames;

    /// <summary>Opens an OPC package from a file path.</summary>
    /// <param name="path">Path to the <c>.msix</c>/<c>.appx</c>/bundle file.</param>
    /// <returns>An open <see cref="OpcPackage"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or empty.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is not a valid ZIP/OPC container.</exception>
    public static OpcPackage Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The package file was not found.", path);
        }

        FileStream fileStream = File.OpenRead(path);
        try
        {
            return Open(fileStream, leaveOpen: false);
        }
        catch
        {
            fileStream.Dispose();
            throw;
        }
    }

    /// <summary>Opens an OPC package from a seekable stream.</summary>
    /// <param name="stream">A readable, seekable stream positioned at the start of the package.</param>
    /// <param name="leaveOpen">Whether to leave <paramref name="stream"/> open when this package is disposed.</param>
    /// <returns>An open <see cref="OpcPackage"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The stream is not a valid ZIP/OPC container.</exception>
    public static OpcPackage Open(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen);
        try
        {
            // The central directory is read lazily, so validation failures can surface here.
            return new OpcPackage(archive);
        }
        catch
        {
            // Disposing the archive respects leaveOpen: the caller's stream is preserved when requested.
            archive.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public bool ContainsPart(string partName)
    {
        ArgumentException.ThrowIfNullOrEmpty(partName);
        return _entriesByPart.ContainsKey(NormalizeLookup(partName));
    }

    /// <inheritdoc/>
    public Stream OpenPart(string partName)
    {
        ArgumentException.ThrowIfNullOrEmpty(partName);

        if (!_entriesByPart.TryGetValue(NormalizeLookup(partName), out ZipArchiveEntry? entry))
        {
            throw new FileNotFoundException($"Part '{partName}' was not found in the package.", partName);
        }

        return entry.Open();
    }

    /// <inheritdoc/>
    public void Dispose() => _archive.Dispose();

    /// <summary>
    /// Percent-decodes a raw ZIP entry name into its canonical OPC logical part name. OPC stores part
    /// names percent-encoded (UTF-8), so <c>%21</c> becomes <c>!</c> and <c>%20</c> becomes a space,
    /// matching the unencoded names used by the block map and manifest. Each <c>/</c>-delimited segment
    /// is decoded independently; a segment that decodes to contain a <c>/</c> or <c>\</c> (an encoded
    /// separator, which OPC forbids) causes this to return <see langword="false"/>.
    /// </summary>
    internal static bool TryCanonicalizePartName(string rawName, out string canonical)
    {
        string[] segments = rawName.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            string decoded = Uri.UnescapeDataString(segments[i]);
            if (decoded.Contains('/') || decoded.Contains('\\'))
            {
                canonical = string.Empty;
                return false;
            }

            segments[i] = decoded;
        }

        canonical = string.Join('/', segments);
        return true;
    }

    /// <summary>
    /// Leniently normalizes a caller-supplied part name for lookup: backslashes become forward
    /// slashes and a single leading slash is trimmed. Stored part names are already canonical.
    /// </summary>
    internal static string NormalizeLookup(string name)
    {
        string normalized = name.Replace('\\', '/');
        return normalized.StartsWith('/') ? normalized[1..] : normalized;
    }

    /// <summary>
    /// Validates a raw ZIP entry name against the OPC part-name rules that matter for MSIX: it must
    /// be non-empty, use forward slashes, not be rooted, and contain no empty, <c>.</c>, or <c>..</c>
    /// segments (which also defends against zip-slip style traversal).
    /// </summary>
    internal static bool IsValidPartName(string rawName)
    {
        if (string.IsNullOrEmpty(rawName) || rawName.StartsWith('/') || rawName.Contains('\\'))
        {
            return false;
        }

        foreach (string segment in rawName.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
            {
                return false;
            }
        }

        return true;
    }
}
