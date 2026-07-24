using System.Collections.ObjectModel;

namespace MsixCore.Packaging.Opc;

/// <summary>
/// An <see cref="IOpcPackage"/> backed by a filesystem directory holding an unpacked ("loose")
/// package layout, rather than a ZIP container. This is the foundation for validating and
/// registering loose packages (e.g. from <c>AppxManifest.xml</c> on disk) cross-platform.
/// </summary>
/// <remarks>
/// Part names are the file paths relative to the root, using forward slashes. The directory is read
/// lazily: each <see cref="OpenPart(string)"/> opens the underlying file.
/// </remarks>
public sealed class DirectoryOpcPackage : IOpcPackage
{
    private readonly string _root;
    private readonly Dictionary<string, string> _partToFullPath;
    private readonly ReadOnlyCollection<string> _partNames;

    private DirectoryOpcPackage(string root, Dictionary<string, string> partToFullPath, List<string> partNames)
    {
        _root = root;
        _partToFullPath = partToFullPath;
        _partNames = partNames.AsReadOnly();
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> PartNames => _partNames;

    /// <summary>The absolute root directory of the loose package layout.</summary>
    public string RootDirectory => _root;

    /// <summary>Opens a loose package layout rooted at the given directory.</summary>
    /// <param name="directory">The directory containing the unpacked package (with <c>AppxManifest.xml</c>).</param>
    /// <returns>An open <see cref="DirectoryOpcPackage"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is null or empty.</exception>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    /// <exception cref="InvalidDataException">The directory contains an invalid part name.</exception>
    public static DirectoryOpcPackage Open(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        string root = Path.GetFullPath(directory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"The package directory '{directory}' does not exist.");
        }

        var partToFullPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var partNames = new List<string>();

        foreach (string fullPath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, fullPath).Replace(Path.DirectorySeparatorChar, '/');
            if (Path.AltDirectorySeparatorChar != '/')
            {
                relative = relative.Replace(Path.AltDirectorySeparatorChar, '/');
            }

            if (!OpcPackage.IsValidPartName(relative))
            {
                throw new InvalidDataException($"The package directory contains an invalid part name: '{relative}'.");
            }

            if (!partToFullPath.TryAdd(relative, fullPath))
            {
                throw new InvalidDataException($"The package directory contains a duplicate part name: '{relative}'.");
            }

            partNames.Add(relative);
        }

        return new DirectoryOpcPackage(root, partToFullPath, partNames);
    }

    /// <inheritdoc/>
    public bool ContainsPart(string partName)
    {
        ArgumentException.ThrowIfNullOrEmpty(partName);
        return _partToFullPath.ContainsKey(OpcPackage.NormalizeLookup(partName));
    }

    /// <inheritdoc/>
    public Stream OpenPart(string partName)
    {
        ArgumentException.ThrowIfNullOrEmpty(partName);

        if (!_partToFullPath.TryGetValue(OpcPackage.NormalizeLookup(partName), out string? fullPath))
        {
            throw new FileNotFoundException($"Part '{partName}' was not found in the package.", partName);
        }

        return File.OpenRead(fullPath);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Files are opened on demand; nothing persistent to release.
    }
}
