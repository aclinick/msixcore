using System.IO.Compression;

namespace MsixCore.Packaging.Opc;

/// <summary>
/// Cross-platform <see cref="IOpcPackage"/> implementation backed by
/// <see cref="System.IO.Compression.ZipArchive"/>. Every MSIX/APPX package (and bundle) is an OPC
/// ZIP container, so this is the foundation the rest of the reader builds on.
/// </summary>
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

            string partName = NormalizePartName(entry.FullName);

            // First occurrence wins; a well-formed package never has duplicate part names.
            if (_entriesByPart.TryAdd(partName, entry))
            {
                _partNames.Add(partName);
            }
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
            // ZipArchive takes ownership of the stream (leaveOpen: false), so it is closed on Dispose.
            var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false);
            return new OpcPackage(archive);
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
        return new OpcPackage(archive);
    }

    /// <inheritdoc/>
    public bool ContainsPart(string partName)
    {
        ArgumentException.ThrowIfNullOrEmpty(partName);
        return _entriesByPart.ContainsKey(NormalizePartName(partName));
    }

    /// <inheritdoc/>
    public Stream OpenPart(string partName)
    {
        ArgumentException.ThrowIfNullOrEmpty(partName);

        if (!_entriesByPart.TryGetValue(NormalizePartName(partName), out ZipArchiveEntry? entry))
        {
            throw new FileNotFoundException($"Part '{partName}' was not found in the package.", partName);
        }

        return entry.Open();
    }

    /// <inheritdoc/>
    public void Dispose() => _archive.Dispose();

    /// <summary>
    /// Normalizes a part name to the package-root-relative, forward-slash form used for lookups:
    /// backslashes become forward slashes and any leading slash is removed.
    /// </summary>
    internal static string NormalizePartName(string name)
    {
        string normalized = name.Replace('\\', '/');
        return normalized.StartsWith('/') ? normalized[1..] : normalized;
    }
}
