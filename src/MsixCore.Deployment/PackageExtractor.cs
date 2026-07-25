using MsixCore.Packaging.Opc;

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
        if (new DirectoryInfo(root).Attributes.HasFlag(FileAttributes.ReparsePoint))
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
            var info = new FileInfo(current);
            if (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidDataException(
                    $"Destination path '{current}' contains a symbolic link or junction; refusing to extract.");
            }

            if (Directory.Exists(current)
                && new DirectoryInfo(current).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidDataException(
                    $"Destination path '{current}' contains a symbolic link or junction; refusing to extract.");
            }
        }
    }
}
