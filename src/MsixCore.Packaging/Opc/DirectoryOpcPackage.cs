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

        DirectoryPartEnumeration enumeration = EnumerateValidatedParts(root);
        if (enumeration.Error is not null)
        {
            throw new InvalidDataException(enumeration.Error);
        }

        return new DirectoryOpcPackage(root, enumeration.PartToFullPath, enumeration.PartNames);
    }

    /// <summary>
    /// Recursively enumerates payload files under <paramref name="root"/>, skipping symlinks/reparse
    /// points (both files and directories) so a loose package cannot follow a link outside its root.
    /// </summary>
    internal static IEnumerable<string> EnumeratePayloadFiles(string root)
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

    /// <summary>
    /// Enumerates and validates every part under <paramref name="root"/>. Both opening and drift
    /// detection consume this result, so traversal, normalization, part-name validation,
    /// root-containment validation, and case-insensitive duplicate detection cannot diverge.
    /// </summary>
    /// <remarks>
    /// Reparse points remain deliberately skipped by <see cref="EnumeratePayloadFiles"/>. Any other
    /// validation violation stops enumeration and is returned as an error; callers must fail closed.
    /// </remarks>
    internal static DirectoryPartEnumeration EnumerateValidatedParts(
        string root,
        IEnumerable<string>? payloadFiles = null)
    {
        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        var partToFullPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var partNames = new List<string>();
        foreach (string fullPath in payloadFiles ?? EnumeratePayloadFiles(root))
        {
            string relative = Path.GetRelativePath(root, fullPath)
                .Replace(Path.DirectorySeparatorChar, '/');
            if (Path.AltDirectorySeparatorChar != '/')
            {
                relative = relative.Replace(Path.AltDirectorySeparatorChar, '/');
            }

            if (!OpcPackage.IsValidPartName(relative))
            {
                return DirectoryPartEnumeration.Invalid(
                    $"The package directory contains an invalid part name: '{relative}'.");
            }

            if (!Path.GetFullPath(fullPath).StartsWith(rootWithSeparator, StringComparison.Ordinal))
            {
                return DirectoryPartEnumeration.Invalid(
                    $"The package directory contains a part that escapes the root: '{relative}'.");
            }

            if (!partToFullPath.TryAdd(relative, fullPath))
            {
                return DirectoryPartEnumeration.Invalid(
                    $"The package directory contains a duplicate part name: '{relative}'.");
            }

            partNames.Add(relative);
        }

        return new DirectoryPartEnumeration(partToFullPath, partNames, null);
    }

    internal sealed record DirectoryPartEnumeration(
        Dictionary<string, string> PartToFullPath,
        List<string> PartNames,
        string? Error)
    {
        public static DirectoryPartEnumeration Invalid(string error) =>
            new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), [], error);
    }

    /// <inheritdoc/>
    public string? DetectSnapshotDrift()
    {
        DirectoryPartEnumeration liveEnumeration;
        try
        {
            liveEnumeration = EnumerateValidatedParts(_root);
        }
        catch (IOException ex)
        {
            return $"Failed to re-enumerate the package directory for drift detection: {ex.Message}. Validation cannot be trusted.";
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"Failed to re-enumerate the package directory for drift detection: {ex.Message}. Validation cannot be trusted.";
        }

        if (liveEnumeration.Error is not null)
        {
            return $"{liveEnumeration.Error} The directory has been modified since the package was opened.";
        }

        var liveParts = new HashSet<string>(liveEnumeration.PartNames, StringComparer.OrdinalIgnoreCase);
        foreach (string live in liveParts)
        {
            if (!_partToFullPath.ContainsKey(live))
            {
                return $"Part '{live}' now exists on disk but was absent when the package was opened — the directory has been modified.";
            }
        }

        foreach (string original in _partNames)
        {
            if (!liveParts.Contains(original))
            {
                return $"Part '{original}' was present when the package was opened but is now missing from disk — the directory has been modified.";
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public OpcPartZipInfo? GetZipInfo(string partName)
    {
        ArgumentException.ThrowIfNullOrEmpty(partName);
        return null;
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
