using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using MsixCore.Packaging.Integrity;

namespace MsixCore.Packaging.Authoring;

internal static class BlockMapWriter
{
    private const string BlockMapNamespace = "http://schemas.microsoft.com/appx/2010/blockmap";
    private const string Sha256Uri = "http://www.w3.org/2001/04/xmlenc#sha256";

    public static BlockMapFile CopyAndHash(string name, Stream source, Stream destination)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(BlockMap.BlockSize);
        try
        {
            var blocks = new List<BlockMapBlock>();
            long size = 0;

            while (true)
            {
                int length = ReadBlock(source, buffer);
                if (length == 0)
                {
                    break;
                }

                ReadOnlySpan<byte> block = buffer.AsSpan(0, length);
                destination.Write(block);
                blocks.Add(new BlockMapBlock { Hash = HashToBase64(block) });
                size += length;
            }

            return new BlockMapFile { Name = name, Size = size, Blocks = blocks };
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static CompressedBlockMapFile CompressAndHash(
        string name,
        Stream source,
        Stream destination,
        CompressionLevel compressionLevel)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(BlockMap.BlockSize);
        try
        {
            var blocks = new List<BlockMapBlock>();
            var crc32 = new Crc32Calculator();
            long uncompressedSize = 0;
            long compressedSize = 0;

            while (true)
            {
                int length = ReadBlock(source, buffer);
                if (length == 0)
                {
                    break;
                }

                ReadOnlySpan<byte> block = buffer.AsSpan(0, length);
                crc32.Append(block);
                byte[] compressed = CompressBlock(block, compressionLevel);
                destination.Write(compressed);
                blocks.Add(new BlockMapBlock
                {
                    Hash = HashToBase64(block),
                    CompressedSize = compressed.Length,
                });
                uncompressedSize += length;
                compressedSize += compressed.Length;
            }

            // MakeAppx terminates the single ZIP deflate stream after its independently restartable,
            // full-flushed blocks. The two-byte terminator is entry overhead and is not a Block Size.
            destination.Write([0x03, 0x00]);
            compressedSize += 2;

            return new CompressedBlockMapFile(
                new BlockMapFile { Name = name, Size = uncompressedSize, Blocks = blocks },
                crc32.Value,
                ToUInt32(compressedSize, "compressed size"),
                ToUInt32(uncompressedSize, "uncompressed size"));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static byte[] Write(IReadOnlyList<AuthoredBlockMapFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        using var output = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(output, CreateSettings()))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("BlockMap", BlockMapNamespace);
            writer.WriteAttributeString("HashMethod", Sha256Uri);

            foreach (AuthoredBlockMapFile authoredFile in files)
            {
                BlockMapFile file = authoredFile.File;
                writer.WriteStartElement("File", BlockMapNamespace);
                writer.WriteAttributeString("Name", file.Name.Replace('/', '\\'));
                writer.WriteAttributeString("Size", file.Size.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString(
                    "LfhSize",
                    authoredFile.LocalFileHeaderSize.ToString(CultureInfo.InvariantCulture));

                foreach (BlockMapBlock block in file.Blocks)
                {
                    writer.WriteStartElement("Block", BlockMapNamespace);
                    writer.WriteAttributeString("Hash", block.Hash);
                    if (block.CompressedSize is long compressedSize)
                    {
                        writer.WriteAttributeString(
                            "Size",
                            compressedSize.ToString(CultureInfo.InvariantCulture));
                    }

                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return output.ToArray();
    }

    private static int ReadBlock(Stream source, byte[] buffer)
    {
        // The shared pool may return an array larger than requested; block-map hashes must always
        // be computed over at most the fixed MSIX block size.
        int limit = Math.Min(buffer.Length, BlockMap.BlockSize);
        int filled = 0;
        while (filled < limit)
        {
            int read = source.Read(buffer, filled, limit - filled);
            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        return filled;
    }

    private static byte[] CompressBlock(ReadOnlySpan<byte> block, CompressionLevel compressionLevel)
    {
        using var output = new MemoryStream();
        CompressionLevel effectiveLevel = compressionLevel == CompressionLevel.Optimal
            ? CompressionLevel.SmallestSize
            : compressionLevel;
        using var compressor = new DeflateStream(output, effectiveLevel, leaveOpen: true);
        compressor.Write(block);
        compressor.Flush();
        return output.ToArray();
    }

    private static string HashToBase64(ReadOnlySpan<byte> block)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(block, hash);
        return Convert.ToBase64String(hash);
    }

    private static uint ToUInt32(long value, string description)
    {
        if (value < 0 || value > uint.MaxValue)
        {
            throw new NotSupportedException($"ZIP entry {description} exceeds the non-ZIP64 limit.");
        }

        return (uint)value;
    }

    private static XmlWriterSettings CreateSettings() => new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = false,
        CloseOutput = false,
    };
}

internal sealed record AuthoredBlockMapFile(BlockMapFile File, int LocalFileHeaderSize);

internal sealed record CompressedBlockMapFile(
    BlockMapFile File,
    uint Crc32,
    uint CompressedSize,
    uint UncompressedSize);
