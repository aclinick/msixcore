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

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (Stream source = package.OpenPart(part))
            using (FileStream file = File.Create(target))
            {
                source.CopyTo(file);
            }

            done++;
            progress?.Report(parts.Count == 0 ? 100f : done * 100f / parts.Count);
        }
    }
}
