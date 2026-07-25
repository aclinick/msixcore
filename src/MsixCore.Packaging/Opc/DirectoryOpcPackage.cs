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

        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        foreach (string fullPath in EnumeratePayloadFiles(root))
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

            // Defense in depth: the canonical path must stay within the package root, so a crafted
            // layout cannot expose files outside it.
            if (!Path.GetFullPath(fullPath).StartsWith(rootWithSeparator, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"The package directory contains a part that escapes the root: '{relative}'.");
            }

            if (!partToFullPath.TryAdd(relative, fullPath))
            {
                throw new InvalidDataException($"The package directory contains a duplicate part name: '{relative}'.");
            }

            partNames.Add(relative);
        }

        return new DirectoryOpcPackage(root, partToFullPath, partNames);
    }

    /// <summary>
    /// Recursively enumerates payload files under <paramref name="root"/>, skipping symlinks/reparse
    /// points (both files and directories) so a loose package cannot follow a link outside its root.
    /// </summary>
    private static IEnumerable<string> EnumeratePayloadFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    pending.Push(entry);
                }
                else
                {
                    yield return entry;
                }
            }
        }
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
