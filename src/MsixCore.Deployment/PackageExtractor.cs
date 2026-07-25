using MsixCore.Packaging.Opc;
using MsixCore.Packaging.Integrity;

namespace MsixCore.Deployment;

/// <summary>
/// Extracts an OPC package's parts to a filesystem directory as a loose (unpacked) layout. Pure
/// managed and cross-platform, so it powers both Linux <c>unpack</c> tooling and the install engine.
/// </summary>
public static class PackageExtractor
{
    /// <summary>
    /// Extracts every part of <paramref name="package"/> into <paramref name="destination"/>,
    /// preserving the part path hierarchy.
    /// </summary>
    /// <param name="package">The package to read parts from.</param>
    /// <param name="destination">The target directory (created if missing).</param>
    /// <param name="progress">Optional progress reporter (0–100).</param>
    /// <param name="cancellationToken">Cancellation token; extraction is cooperative.</param>
    /// <exception cref="InvalidDataException">A part name would escape <paramref name="destination"/>.</exception>
    public static void Extract(
        IOpcPackage package,
        string destination,
        IProgress<float>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrEmpty(destination);

        string root = Path.GetFullPath(destination);
        Directory.CreateDirectory(root);

        // The destination root itself must not be a reparse point (symlink/junction); otherwise every
        // write below it is silently redirected outside the intended tree even though each part path
        // looks contained. The per-part walk below only inspects segments *beneath* the root, so the
        // root has to be validated explicitly here, before any extraction begins.
        if (IsReparsePoint(root))
        {
            throw new InvalidDataException(
                $"Destination directory '{root}' is a symbolic link or junction; refusing to extract.");
        }

        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        var parts = package.PartNames.ToList();
        int done = 0;
        foreach (string part in parts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string relative = part.Replace('/', Path.DirectorySeparatorChar);
            string target = Path.GetFullPath(Path.Combine(root, relative));

            // A malicious/malformed package must never write outside the destination.
            if (!target.StartsWith(rootWithSeparator, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Package part '{part}' resolves outside the destination directory.");
            }

            // Defense against symlink/junction escape: a link anywhere between the root and the target
            // (or the target file itself) could redirect the write outside the root even though the
            // lexical path looks contained.
            EnsureNoReparsePointEscape(root, target);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (Stream source = package.OpenPart(part))
            using (FileStream file = File.Create(target))
            {
                CopyCancelable(source, file, cancellationToken);
            }

            done++;
            progress?.Report(parts.Count == 0 ? 100f : done * 100f / parts.Count);
        }
    }

    /// <summary>
    /// Extracts a package while verifying each payload file against its block map in the same read.
    /// </summary>
    public static BlockMapVerificationResult ExtractAndVerify(
        IOpcPackage package,
        BlockMap blockMap,
        string destination,
        IProgress<float>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(blockMap);
        ArgumentException.ThrowIfNullOrEmpty(destination);

        IReadOnlyList<string> coverageErrors = BlockMapVerifier.VerifyCoverage(package, blockMap);
        if (coverageErrors.Count != 0)
        {
            return new BlockMapVerificationResult
            {
                IsValid = false,
                Files = [],
                CoverageErrors = coverageErrors,
            };
        }

        var mappedFiles = blockMap.Files.ToDictionary(static file => file.Name, StringComparer.OrdinalIgnoreCase);
        var fileResults = new Dictionary<string, BlockMapFileResult>(StringComparer.OrdinalIgnoreCase);
        string root = PrepareDestination(destination);
        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        var parts = package.PartNames.ToList();
        int done = 0;

        foreach (string part in parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = GetContainedTarget(root, rootWithSeparator, part);
            EnsureNoReparsePointEscape(root, target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            using Stream source = package.OpenPart(part);
            using FileStream file = File.Create(target);
            if (mappedFiles.TryGetValue(part, out BlockMapFile? mapped))
            {
                BlockMapFileResult result = BlockMapVerifier.VerifyAndCopy(
                    source,
                    file,
                    blockMap.HashMethod,
                    mapped,
                    cancellationToken);
                fileResults.Add(result.Name, result);
                if (!result.IsValid)
                {
                    return new BlockMapVerificationResult
                    {
                        IsValid = false,
                        Files = blockMap.Files
                            .Where(mappedFile => fileResults.ContainsKey(mappedFile.Name))
                            .Select(mappedFile => fileResults[mappedFile.Name])
                            .ToList(),
                        CoverageErrors = [],
                    };
                }
            }
            else
            {
                CopyCancelable(source, file, cancellationToken);
            }

            done++;
            progress?.Report(parts.Count == 0 ? 100f : done * 100f / parts.Count);
        }

        return new BlockMapVerificationResult
        {
            IsValid = true,
            Files = blockMap.Files.Select(mappedFile => fileResults[mappedFile.Name]).ToList(),
            CoverageErrors = [],
        };
    }

    /// <summary>Copies <paramref name="source"/> to <paramref name="destination"/> honoring cancellation between chunks.</summary>
    private static void CopyCancelable(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                destination.Write(buffer, 0, read);
            }

        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string PrepareDestination(string destination)
    {
        string root = Path.GetFullPath(destination);
        Directory.CreateDirectory(root);
        if (IsReparsePoint(root))
        {
            throw new InvalidDataException(
                $"Destination directory '{root}' is a symbolic link or junction; refusing to extract.");
        }

        return root;
    }

    private static string GetContainedTarget(string root, string rootWithSeparator, string part)
    {
        string relative = part.Replace('/', Path.DirectorySeparatorChar);
        string target = Path.GetFullPath(Path.Combine(root, relative));
        if (!target.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Package part '{part}' resolves outside the destination directory.");
        }

        return target;
    }

    /// <summary>
    /// Rejects extraction when any existing directory between <paramref name="root"/> and
    /// <paramref name="target"/>, or the target file itself, is a reparse point (symlink/junction),
    /// which could redirect writes outside the root.
    /// </summary>
    private static void EnsureNoReparsePointEscape(string root, string target)
    {
        string relative = Path.GetRelativePath(root, target);
        string current = root;
        foreach (string segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Length == 0)
            {
                continue;
            }

            current = Path.Combine(current, segment);
            if (IsReparsePoint(current))
            {
                throw new InvalidDataException(
                    $"Destination path '{current}' contains a symbolic link or junction; refusing to extract.");
            }
        }
    }

    /// <summary>
    /// Reports whether <paramref name="path"/> is a symbolic link or junction, using no-follow link
    /// metadata so it is detected even when the link's target does not exist. This is deliberately not
    /// gated on <see cref="FileSystemInfo.Exists"/>, which reports <see langword="false"/> for a
    /// dangling link and would otherwise let it slip through and redirect the subsequent write.
    /// </summary>
    private static bool IsReparsePoint(string path) =>
        HasLinkTarget(new FileInfo(path)) || HasLinkTarget(new DirectoryInfo(path));

    private static bool HasLinkTarget(FileSystemInfo info)
    {
        try
        {
            return info.LinkTarget is not null;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
