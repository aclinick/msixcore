using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using MsixCore.Packaging.Opc;

namespace MsixCore.CorpusRoundtrip;

/// <summary>A block-map block with its hash and optional stored size.</summary>
public sealed record SemanticBlock(string Hash, long? Size);

/// <summary>A block-map file with LfhSize, uncompressed size, and ordered blocks.</summary>
public sealed record SemanticBlockMapFile(string Name, long LfhSize, long Size, IReadOnlyList<SemanticBlock> Blocks);

/// <summary>Parsed block-map details needed for makeappx compatibility comparisons.</summary>
public sealed record SemanticBlockMap(IReadOnlyList<SemanticBlockMapFile> Files);

/// <summary>A human-readable semantic AppxBlockMap.xml difference.</summary>
public sealed record BlockMapSemanticDifference(string FileName, string Field, string Left, string Right, string Interpretation);

/// <summary>Result of comparing two AppxBlockMap.xml documents.</summary>
public sealed record BlockMapSemanticDiffResult(bool IsEquivalent, IReadOnlyList<BlockMapSemanticDifference> Differences);

/// <summary>Parses and compares AppxBlockMap.xml semantics.</summary>
public sealed class BlockMapSemanticDiffer
{
    private static readonly XmlReaderSettings SafeReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        CloseInput = false,
    };

    /// <summary>Compares block maps embedded in two package files.</summary>
    public static BlockMapSemanticDiffResult ComparePackages(string leftPackagePath, string rightPackagePath, bool includeLfhSizeAndBlockSizes)
    {
        SemanticBlockMap left = ReadFromPackage(leftPackagePath);
        SemanticBlockMap right = ReadFromPackage(rightPackagePath);
        return Compare(left, right, includeLfhSizeAndBlockSizes);
    }

    /// <summary>Compares two parsed block maps.</summary>
    public static BlockMapSemanticDiffResult Compare(
        SemanticBlockMap left,
        SemanticBlockMap right,
        bool includeLfhSizeAndBlockSizes)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var differences = new List<BlockMapSemanticDifference>();
        var leftFiles = left.Files.ToDictionary(static file => file.Name, StringComparer.OrdinalIgnoreCase);
        var rightFiles = right.Files.ToDictionary(static file => file.Name, StringComparer.OrdinalIgnoreCase);

        foreach (string name in leftFiles.Keys.Union(rightFiles.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(static name => name, StringComparer.Ordinal))
        {
            bool hasLeft = leftFiles.TryGetValue(name, out SemanticBlockMapFile? leftFile);
            bool hasRight = rightFiles.TryGetValue(name, out SemanticBlockMapFile? rightFile);
            if (!hasLeft || !hasRight)
            {
                differences.Add(new BlockMapSemanticDifference(
                    name,
                    "presence",
                    hasLeft ? "present" : "missing",
                    hasRight ? "present" : "missing",
                    "The block map covers a different set of files."));
                continue;
            }

            CompareFile(differences, leftFile!, rightFile!, includeLfhSizeAndBlockSizes);
        }

        return new BlockMapSemanticDiffResult(differences.Count == 0, differences);
    }

    /// <summary>Parses an AppxBlockMap.xml stream.</summary>
    public static SemanticBlockMap Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using XmlReader reader = XmlReader.Create(stream, SafeReaderSettings);
        XDocument document = XDocument.Load(reader);
        XElement root = document.Root ?? throw new InvalidDataException("The block map has no root element.");
        if (root.Name.LocalName != "BlockMap")
        {
            throw new InvalidDataException("The block map root element is not 'BlockMap'.");
        }

        var files = new List<SemanticBlockMapFile>();
        foreach (XElement file in root.Elements().Where(static element => element.Name.LocalName == "File"))
        {
            string name = Required(file, "Name").Replace('\\', '/');
            long lfhSize = ParseLong(Required(file, "LfhSize"), name, "LfhSize");
            long size = ParseLong(Required(file, "Size"), name, "Size");
            var blocks = new List<SemanticBlock>();
            foreach (XElement block in file.Elements().Where(static element => element.Name.LocalName == "Block"))
            {
                string hash = Required(block, "Hash");
                string? sizeText = block.Attribute("Size")?.Value;
                blocks.Add(new SemanticBlock(hash, sizeText is null ? null : ParseLong(sizeText, name, "Block Size")));
            }

            files.Add(new SemanticBlockMapFile(name, lfhSize, size, blocks));
        }

        return new SemanticBlockMap(files);
    }

    private static SemanticBlockMap ReadFromPackage(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry entry = archive.GetEntry(OpcPartNames.AppxBlockMap)
            ?? throw new InvalidDataException("The package does not contain AppxBlockMap.xml.");
        using Stream stream = entry.Open();
        return Parse(stream);
    }

    private static void CompareFile(
        List<BlockMapSemanticDifference> differences,
        SemanticBlockMapFile left,
        SemanticBlockMapFile right,
        bool includeLfhSizeAndBlockSizes)
    {
        if (includeLfhSizeAndBlockSizes && left.LfhSize != right.LfhSize)
        {
            differences.Add(new BlockMapSemanticDifference(
                left.Name,
                "LfhSize",
                left.LfhSize.ToString(CultureInfo.InvariantCulture),
                right.LfhSize.ToString(CultureInfo.InvariantCulture),
                "ZIP local-file-header size semantics differ."));
        }

        if (left.Size != right.Size)
        {
            differences.Add(new BlockMapSemanticDifference(
                left.Name,
                "Size",
                left.Size.ToString(CultureInfo.InvariantCulture),
                right.Size.ToString(CultureInfo.InvariantCulture),
                "The uncompressed file size differs."));
        }

        if (left.Blocks.Count != right.Blocks.Count)
        {
            differences.Add(new BlockMapSemanticDifference(
                left.Name,
                "block count",
                left.Blocks.Count.ToString(CultureInfo.InvariantCulture),
                right.Blocks.Count.ToString(CultureInfo.InvariantCulture),
                "The block map splits the file differently or hashes different content."));
        }

        int shared = Math.Min(left.Blocks.Count, right.Blocks.Count);
        for (int i = 0; i < shared; i++)
        {
            SemanticBlock leftBlock = left.Blocks[i];
            SemanticBlock rightBlock = right.Blocks[i];
            if (!string.Equals(leftBlock.Hash, rightBlock.Hash, StringComparison.Ordinal))
            {
                differences.Add(new BlockMapSemanticDifference(
                    left.Name,
                    "Block[" + i.ToString(CultureInfo.InvariantCulture) + "].Hash",
                    leftBlock.Hash,
                    rightBlock.Hash,
                    "The uncompressed data hash differs."));
            }

            if (includeLfhSizeAndBlockSizes && leftBlock.Size != rightBlock.Size)
            {
                differences.Add(new BlockMapSemanticDifference(
                    left.Name,
                    "Block[" + i.ToString(CultureInfo.InvariantCulture) + "].Size",
                    leftBlock.Size?.ToString(CultureInfo.InvariantCulture) ?? "<absent>",
                    rightBlock.Size?.ToString(CultureInfo.InvariantCulture) ?? "<absent>",
                    "The stored/compressed block size differs."));
            }
        }
    }

    private static string Required(XElement element, string attributeName) =>
        element.Attribute(attributeName)?.Value
        ?? throw new InvalidDataException("A block-map element is missing required attribute '" + attributeName + "'.");

    private static long ParseLong(string text, string fileName, string field)
    {
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long value))
        {
            throw new InvalidDataException("File '" + fileName + "' has invalid " + field + " value '" + text + "'.");
        }

        return value;
    }
}
