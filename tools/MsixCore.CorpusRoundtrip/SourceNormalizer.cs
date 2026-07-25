using MsixCore.Packaging;
using MsixCore.Packaging.Opc;

namespace MsixCore.CorpusRoundtrip;

/// <summary>Extracts package payload plus manifest into a deterministic source layout.</summary>
public sealed class SourceNormalizer
{
    /// <summary>Fixed timestamp used for every normalized source file.</summary>
    public static readonly DateTimeOffset FixedTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Normalizes a package file or loose package directory into <paramref name="destinationDirectory"/>.</summary>
    public static NormalizedSource Normalize(string inputPath, string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(inputPath);
        ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);

        if (Directory.Exists(destinationDirectory))
        {
            Directory.Delete(destinationDirectory, recursive: true);
        }

        Directory.CreateDirectory(destinationDirectory);
        using MsixPackage package = Directory.Exists(inputPath)
            ? MsixPackage.OpenDirectory(inputPath)
            : MsixPackage.Open(inputPath);

        string root = Path.GetFullPath(destinationDirectory);
        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        var copied = new List<string>();
        foreach (string partName in package.Opc.PartNames.OrderBy(static name => name, StringComparer.Ordinal))
        {
            if (PackageFootprints.IsFootprint(partName))
            {
                continue;
            }

            string target = GetContainedTarget(root, rootWithSeparator, partName);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (Stream source = package.Opc.OpenPart(partName))
            using (FileStream destination = File.Create(target))
            {
                source.CopyTo(destination);
            }

            File.SetLastWriteTimeUtc(target, FixedTimestamp.UtcDateTime);
            copied.Add(partName);
        }

        return new NormalizedSource(root, copied);
    }

    private static string GetContainedTarget(string root, string rootWithSeparator, string partName)
    {
        string relative = partName.Replace('/', Path.DirectorySeparatorChar);
        string target = Path.GetFullPath(Path.Combine(root, relative));
        if (!target.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Package part '{partName}' resolves outside the normalized source directory.");
        }

        return target;
    }
}

/// <summary>A normalized loose package source layout.</summary>
public sealed record NormalizedSource(string DirectoryPath, IReadOnlyList<string> PartNames);
