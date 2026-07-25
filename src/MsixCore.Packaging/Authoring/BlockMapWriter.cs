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

            // Write compressed output directly to the destination through a gated passthrough
            // stream.  The gate blocks writes during DeflateStream.Dispose() so the deflate
            // finalization marker does not reach the output — MSIX blocks are sync-flushed,
            // not finalized.  This eliminates the per-block MemoryStream, ToArray(), and
            // buffer-to-destination copy that were the dominant allocation sources.
            var gate = new GatedCountingStream(destination);

            while (true)
            {
                int length = ReadBlock(source, buffer);
                if (length == 0)
                {
                    break;
                }

                ReadOnlySpan<byte> block = buffer.AsSpan(0, length);
                crc32.Append(block);

                gate.Reset();
                int compressedLength = CompressBlockDirect(block, compressionLevel, gate);

                blocks.Add(new BlockMapBlock
                {
                    Hash = HashToBase64(block),
                    CompressedSize = compressedLength,
                });
                uncompressedSize += length;
                compressedSize += compressedLength;
            }

            // MakeAppx terminates the single ZIP deflate stream after its independently restartable,
            // full-flushed blocks. The two-byte terminator is entry overhead and is not a Block Size.
            destination.Write([0x03, 0x00]);
            compressedSize += 2;

            return new CompressedBlockMapFile(
                new BlockMapFile { Name = name, Size = uncompressedSize, Blocks = blocks },
                crc32.Value,
                compressedSize,
                uncompressedSize);
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

    /// <summary>
    /// Compresses a single block directly to <paramref name="gate"/>, which wraps the real
    /// destination.  Returns the compressed byte count.  The gate is closed before
    /// <see cref="DeflateStream.Dispose"/> so the deflate finalization marker is swallowed
    /// — MSIX blocks use sync-flush, not stream finalization.
    /// </summary>
    private static int CompressBlockDirect(
        ReadOnlySpan<byte> block,
        CompressionLevel compressionLevel,
        GatedCountingStream gate)
    {
        CompressionLevel effectiveLevel = compressionLevel == CompressionLevel.Optimal
            ? CompressionLevel.SmallestSize
            : compressionLevel;
        var compressor = new DeflateStream(gate, effectiveLevel, leaveOpen: true);
        compressor.Write(block);
        compressor.Flush();
        int compressedLength = gate.BytesWritten;
        gate.Close();
        compressor.Dispose();
        return compressedLength;
    }

    private static string HashToBase64(ReadOnlySpan<byte> block)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(block, hash);
        return Convert.ToBase64String(hash);
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
    long CompressedSize,
    long UncompressedSize);

/// <summary>
/// Write-only passthrough stream that counts bytes written to an inner stream and can be
/// "closed" (gated) so that subsequent writes are silently discarded.  Used to let
/// <see cref="DeflateStream"/> write compressed data directly to the destination while
/// blocking the finalization bytes that <see cref="DeflateStream.Dispose"/> emits.
/// Reusable across blocks via <see cref="Reset"/>.
/// </summary>
internal sealed class GatedCountingStream : Stream
{
    private readonly Stream _inner;
    private int _bytesWritten;
    private bool _open = true;

    public GatedCountingStream(Stream inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public int BytesWritten => _bytesWritten;

    /// <summary>Resets the byte counter and re-opens the gate for the next block.</summary>
    public void Reset()
    {
        _bytesWritten = 0;
        _open = true;
    }

    /// <summary>Closes the gate so that subsequent writes are discarded.</summary>
    public new void Close() => _open = false;

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (!_open)
        {
            return;
        }

        _inner.Write(buffer);
        _bytesWritten += buffer.Length;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Write(buffer.AsSpan(offset, count));
    }

    public override void WriteByte(byte value)
    {
        if (!_open)
        {
            return;
        }

        _inner.WriteByte(value);
        _bytesWritten++;
    }

    public override void Flush()
    {
        // Intentionally a no-op.  DeflateStream.Flush() calls _stream.Flush() after
        // flushing the deflate engine.  The real destination stream is flushed externally
        // by the caller (StoredZipWriter.Dispose).  Suppressing per-block file flushes
        // avoids a measurable wall-clock penalty.
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
