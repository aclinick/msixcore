using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using MsixCore.Packaging.Manifest;

namespace MsixCore.Packaging.Integrity;

/// <summary>
/// Parses an <c>AppxBlockMap.xml</c> document into a <see cref="BlockMap"/>.
/// </summary>
/// <remarks>
/// Like the manifest parsers this is namespace-tolerant and hardened against XXE
/// (<see cref="DtdProcessing.Prohibit"/>, no external resolver). File names are normalized from the
/// block map's native backslash separators to forward slashes to match OPC part names.
/// </remarks>
public static class BlockMapParser
{
    private static readonly XmlReaderSettings SafeReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        CloseInput = false,
    };

    /// <summary>Parses a block map from a stream.</summary>
    /// <param name="blockMapStream">A readable stream over an <c>AppxBlockMap.xml</c> document.</param>
    /// <returns>The parsed block map.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="blockMapStream"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The block map is malformed or missing required data.</exception>
    public static BlockMap Parse(Stream blockMapStream)
    {
        ArgumentNullException.ThrowIfNull(blockMapStream);

        XDocument document;
        try
        {
            using XmlReader reader = XmlReader.Create(blockMapStream, SafeReaderSettings);
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException("The block map is not well-formed XML.", ex);
        }

        return Parse(document);
    }

    /// <summary>Parses a block map from an already-loaded XML document.</summary>
    /// <param name="document">The block-map document; its root must be a <c>BlockMap</c> element.</param>
    /// <returns>The parsed block map.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The block map is malformed or missing required data.</exception>
    public static BlockMap Parse(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        XElement root = document.Root
            ?? throw new InvalidDataException("The block map has no root element.");

        if (root.Name.LocalName != "BlockMap")
        {
            throw new InvalidDataException(
                $"Expected a 'BlockMap' root element but found '{root.Name.LocalName}'.");
        }

        return new BlockMap
        {
            HashMethod = ParseHashMethod(root.AttributeValue("HashMethod")),
            Files = ParseFiles(root),
        };
    }

    internal static BlockMapHashMethod ParseHashMethod(string? value) => value switch
    {
        "http://www.w3.org/2001/04/xmlenc#sha256" => BlockMapHashMethod.Sha256,
        "http://www.w3.org/2001/04/xmldsig-more#sha384" => BlockMapHashMethod.Sha384,
        "http://www.w3.org/2001/04/xmlenc#sha512" => BlockMapHashMethod.Sha512,
        null or "" => throw new InvalidDataException("The block map is missing the required 'HashMethod' attribute."),
        _ => throw new InvalidDataException($"The block map declares an unsupported HashMethod '{value}'."),
    };

    private static List<BlockMapFile> ParseFiles(XElement root)
    {
        var files = new List<BlockMapFile>();
        foreach (XElement file in root.ElementsByLocalName("File"))
        {
            string name = file.AttributeValue("Name")
                ?? throw new InvalidDataException("A block map 'File' is missing the required 'Name' attribute.");
            long size = ParseNonNegativeLong(file.AttributeValue("Size"), $"File '{name}' Size");

            var blocks = new List<BlockMapBlock>();
            foreach (XElement block in file.ElementsByLocalName("Block"))
            {
                string hash = block.AttributeValue("Hash")
                    ?? throw new InvalidDataException($"A 'Block' in file '{name}' is missing the required 'Hash' attribute.");

                long? compressedSize = null;
                string? sizeText = block.AttributeValue("Size");
                if (sizeText is not null)
                {
                    compressedSize = ParseNonNegativeLong(sizeText, $"Block Size in file '{name}'");
                }

                blocks.Add(new BlockMapBlock { Hash = hash, CompressedSize = compressedSize });
            }

            files.Add(new BlockMapFile
            {
                Name = NormalizeName(name),
                Size = size,
                Blocks = blocks,
            });
        }

        return files;
    }

    private static string NormalizeName(string name) => name.Replace('\\', '/');

    private static long ParseNonNegativeLong(string? value, string context)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long result))
        {
            throw new InvalidDataException($"{context} has an invalid non-negative integer value '{value}'.");
        }

        return result;
    }
}
