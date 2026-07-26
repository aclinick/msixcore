using System.Buffers.Binary;
using System.Text;

namespace MsixCore.Packaging.Authoring;

/// <summary>
/// ZIP writer that always emits a ZIP64 end-of-central-directory structure (matching the
/// microsoft/msix-packaging SDK behaviour). Per-entry ZIP64 extra fields and data descriptors
/// are used only when an entry's sizes or offset overflow 32-bit limits.
/// </summary>
internal sealed class StoredZipWriter : IDisposable
{
    private const uint LocalFileHeaderSignature = 0x04034B50;
    private const uint CentralDirectoryHeaderSignature = 0x02014B50;
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const uint Zip64EocdSignature = 0x06064B50;
    private const uint Zip64EocdLocatorSignature = 0x07064B50;
    private const uint DataDescriptorSignature = 0x08074B50;
    private const ushort Zip64ExtraFieldId = 0x0001;
    private const ushort Version20 = 20;
    private const ushort Version45 = 45;
    private const ushort StoredMethod = 0;
    private const ushort DeflateMethod = 8;
    private const ushort DosTime = 0;
    private const ushort DosDate = 0x0021;

    // The SDK uses data descriptors only when a size exceeds this threshold.
    internal const long MaxSizeToNotUseDataDescriptor = (long)uint.MaxValue - 1;
    private const ushort DataDescriptorFlag = 0x0008;

    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    private readonly Stream _output;
    private readonly List<CentralDirectoryEntry> _entries = [];
    private bool _disposed;

    public StoredZipWriter(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite || !output.CanSeek)
        {
            throw new ArgumentException("The ZIP output stream must be writable and seekable.", nameof(output));
        }

        _output = output;
    }

    public StoredZipEntryInfo AddEntry(string entryName, Action<Stream> writeContent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(entryName);
        ArgumentNullException.ThrowIfNull(writeContent);

        byte[] nameBytes = Utf8.GetBytes(entryName);
        int localHeaderSize = 30 + nameBytes.Length;
        if (nameBytes.Length > ushort.MaxValue || localHeaderSize > ushort.MaxValue)
        {
            throw MsixError.Format(MsixErrorCode.PartName, $"The ZIP entry name '{entryName}' is too long.");
        }

        long localHeaderOffset = _output.Position;
        WriteUInt32(LocalFileHeaderSignature);
        WriteUInt16(Version20); // patched to 45 below if data descriptor needed
        WriteUInt16(0); // general purpose flags — patched below if data descriptor needed
        WriteUInt16(StoredMethod);
        WriteUInt16(DosTime);
        WriteUInt16(DosDate);
        WriteUInt32(0); // CRC-32 placeholder
        WriteUInt32(0); // compressed size placeholder
        WriteUInt32(0); // uncompressed size placeholder
        WriteUInt16((ushort)nameBytes.Length);
        WriteUInt16(0); // extra field length
        _output.Write(nameBytes);

        var entryStream = new StoredEntryStream(_output);
        writeContent(entryStream);
        long size = entryStream.BytesWritten;
        uint crc32 = entryStream.Crc32;

        bool needsDataDescriptor = size > MaxSizeToNotUseDataDescriptor;
        if (needsDataDescriptor)
        {
            // Emit ZIP64 data descriptor: signature + CRC-32 + 8-byte sizes.
            WriteUInt32(DataDescriptorSignature);
            WriteUInt32(crc32);
            WriteUInt64((ulong)size);
            WriteUInt64((ulong)size);

            // Seek back to set version-needed 45 and data-descriptor flag in local header.
            long endPosition = _output.Position;
            _output.Position = localHeaderOffset + 4;
            WriteUInt16(Version45);
            WriteUInt16(DataDescriptorFlag);
            _output.Position = endPosition;
        }
        else
        {
            // Seek back to patch CRC and sizes in the local file header.
            long endPosition = _output.Position;
            _output.Position = localHeaderOffset + 14;
            WriteUInt32(crc32);
            WriteUInt32((uint)size);
            WriteUInt32((uint)size);
            _output.Position = endPosition;
        }

        _entries.Add(new CentralDirectoryEntry(
            nameBytes,
            StoredMethod,
            crc32,
            size,
            size,
            localHeaderOffset,
            needsDataDescriptor));
        return new StoredZipEntryInfo(localHeaderSize, localHeaderOffset + localHeaderSize, size);
    }

    public StoredZipEntryInfo AddDeflatedEntry(
        string entryName,
        Func<Stream, DeflatedZipEntryContent> writeContent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(entryName);
        ArgumentNullException.ThrowIfNull(writeContent);

        byte[] nameBytes = Utf8.GetBytes(entryName);
        int localHeaderSize = 30 + nameBytes.Length;
        if (nameBytes.Length > ushort.MaxValue || localHeaderSize > ushort.MaxValue)
        {
            throw MsixError.Format(MsixErrorCode.PartName, $"The ZIP entry name '{entryName}' is too long.");
        }

        long localHeaderOffset = _output.Position;
        WriteUInt32(LocalFileHeaderSignature);
        WriteUInt16(Version20); // patched to 45 below if data descriptor needed
        WriteUInt16(0); // general purpose flags — patched below if data descriptor needed
        WriteUInt16(DeflateMethod);
        WriteUInt16(DosTime);
        WriteUInt16(DosDate);
        WriteUInt32(0); // CRC-32 placeholder
        WriteUInt32(0); // compressed size placeholder
        WriteUInt32(0); // uncompressed size placeholder
        WriteUInt16((ushort)nameBytes.Length);
        WriteUInt16(0); // extra field length
        _output.Write(nameBytes);

        long contentOffset = _output.Position;
        DeflatedZipEntryContent content = writeContent(_output);
        long actualCompressedSize = _output.Position - contentOffset;
        if (actualCompressedSize != content.CompressedSize)
        {
            throw MsixError.Format(MsixErrorCode.ZipStructure,
                $"ZIP entry '{entryName}' wrote {actualCompressedSize} compressed bytes but reported {content.CompressedSize}.");
        }

        bool needsDataDescriptor =
            content.CompressedSize > MaxSizeToNotUseDataDescriptor ||
            content.UncompressedSize > MaxSizeToNotUseDataDescriptor;

        if (needsDataDescriptor)
        {
            WriteUInt32(DataDescriptorSignature);
            WriteUInt32(content.Crc32);
            WriteUInt64((ulong)content.CompressedSize);
            WriteUInt64((ulong)content.UncompressedSize);

            // Seek back to set version-needed 45 and data-descriptor flag in local header.
            long endPosition = _output.Position;
            _output.Position = localHeaderOffset + 4;
            WriteUInt16(Version45);
            WriteUInt16(DataDescriptorFlag);
            _output.Position = endPosition;
        }
        else
        {
            long endPosition = _output.Position;
            _output.Position = localHeaderOffset + 14;
            WriteUInt32(content.Crc32);
            WriteUInt32((uint)content.CompressedSize);
            WriteUInt32((uint)content.UncompressedSize);
            _output.Position = endPosition;
        }

        _entries.Add(new CentralDirectoryEntry(
            nameBytes,
            DeflateMethod,
            content.Crc32,
            content.CompressedSize,
            content.UncompressedSize,
            localHeaderOffset,
            needsDataDescriptor));
        return new StoredZipEntryInfo(
            localHeaderSize,
            localHeaderOffset + localHeaderSize,
            content.UncompressedSize);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        long centralDirectoryOffset = _output.Position;
        foreach (CentralDirectoryEntry entry in _entries)
        {
            byte[] extraField = BuildCentralZip64ExtraField(entry);
            bool hasZip64Extra = extraField.Length > 0;
            // Version-needed is 45 if the entry has a ZIP64 extra field OR a data descriptor.
            ushort versionNeeded = (hasZip64Extra || entry.HasDataDescriptor) ? Version45 : Version20;
            ushort flags = entry.HasDataDescriptor ? DataDescriptorFlag : (ushort)0;

            WriteUInt32(CentralDirectoryHeaderSignature);
            WriteUInt16(Version45); // version made by
            WriteUInt16(versionNeeded);
            WriteUInt16(flags);
            WriteUInt16(entry.CompressionMethod);
            WriteUInt16(DosTime);
            WriteUInt16(DosDate);
            WriteUInt32(entry.Crc32);
            WriteUInt32(IsOverflow32(entry.CompressedSize) ? uint.MaxValue : (uint)entry.CompressedSize);
            WriteUInt32(IsOverflow32(entry.UncompressedSize) ? uint.MaxValue : (uint)entry.UncompressedSize);
            WriteUInt16((ushort)entry.NameBytes.Length);
            WriteUInt16((ushort)extraField.Length);
            WriteUInt16(0); // comment length
            WriteUInt16(0); // disk number start
            WriteUInt16(0); // internal file attributes
            WriteUInt32(0); // external file attributes
            WriteUInt32(IsOverflow32(entry.LocalHeaderOffset) ? uint.MaxValue : (uint)entry.LocalHeaderOffset);
            _output.Write(entry.NameBytes);
            if (extraField.Length > 0)
            {
                _output.Write(extraField);
            }
        }

        long centralDirectorySize = _output.Position - centralDirectoryOffset;

        // ZIP64 End of Central Directory Record (always emitted, per SDK model).
        long zip64EocdOffset = _output.Position;
        long entryCount = _entries.Count;
        WriteUInt32(Zip64EocdSignature);
        WriteUInt64(44); // size of remaining ZIP64 EOCD (fixed: 2+2+4+4+8+8+8+8 = 44)
        WriteUInt16(Version45); // version made by
        WriteUInt16(Version45); // version needed
        WriteUInt32(0); // disk number
        WriteUInt32(0); // disk with CD start
        WriteUInt64((ulong)entryCount); // entries on this disk
        WriteUInt64((ulong)entryCount); // total entries
        WriteUInt64((ulong)centralDirectorySize);
        WriteUInt64((ulong)centralDirectoryOffset);

        // ZIP64 End of Central Directory Locator.
        WriteUInt32(Zip64EocdLocatorSignature);
        WriteUInt32(0); // disk with ZIP64 EOCD
        WriteUInt64((ulong)zip64EocdOffset);
        WriteUInt32(1); // total disks

        // Classic End of Central Directory Record — unconditional sentinel constants.
        // The SDK's EndCentralDirectoryRecord::Read derives m_isZip64 EXCLUSIVELY from
        // whether any classic EOCD field equals its type-maximum sentinel. If we write
        // real values here, the SDK reads m_isZip64 == false and ZipObjectWriter's editing
        // constructor throws "Editing non zip64 packages not supported" — signing fails.
        WriteUInt32(EndOfCentralDirectorySignature);
        WriteUInt16(0);           // number of this disk
        WriteUInt16(0);           // disk with start of CD
        WriteUInt16(0xFFFF);      // entries on this disk (sentinel)
        WriteUInt16(0xFFFF);      // total entries (sentinel)
        WriteUInt32(0xFFFFFFFF);  // size of central directory (sentinel)
        WriteUInt32(0xFFFFFFFF);  // offset of start of CD (sentinel)
        WriteUInt16(0);           // comment length

        _output.Flush();
        _disposed = true;
    }

    /// <summary>
    /// Builds a variable-sized ZIP64 extra field (ID 0x0001) for a central directory entry,
    /// including only the members that overflow their 32-bit field. Returns empty if no overflow.
    /// Order: uncompressed size, compressed size, relative offset, disk start (per APPNOTE).
    /// </summary>
    internal static byte[] BuildCentralZip64ExtraField(
        long uncompressedSize,
        long compressedSize,
        long localHeaderOffset)
    {
        int dataSize = 0;
        if (IsOverflow32(uncompressedSize)) dataSize += 8;
        if (IsOverflow32(compressedSize)) dataSize += 8;
        if (IsOverflow32(localHeaderOffset)) dataSize += 8;

        if (dataSize == 0)
        {
            return [];
        }

        byte[] extra = new byte[4 + dataSize]; // 2-byte ID + 2-byte data size + data
        BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(0), Zip64ExtraFieldId);
        BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(2), (ushort)dataSize);
        int offset = 4;
        if (IsOverflow32(uncompressedSize))
        {
            BinaryPrimitives.WriteUInt64LittleEndian(extra.AsSpan(offset), (ulong)uncompressedSize);
            offset += 8;
        }
        if (IsOverflow32(compressedSize))
        {
            BinaryPrimitives.WriteUInt64LittleEndian(extra.AsSpan(offset), (ulong)compressedSize);
            offset += 8;
        }
        if (IsOverflow32(localHeaderOffset))
        {
            BinaryPrimitives.WriteUInt64LittleEndian(extra.AsSpan(offset), (ulong)localHeaderOffset);
        }

        return extra;
    }

    private static byte[] BuildCentralZip64ExtraField(CentralDirectoryEntry entry) =>
        BuildCentralZip64ExtraField(entry.UncompressedSize, entry.CompressedSize, entry.LocalHeaderOffset);

    private static bool IsOverflow32(long value) => value > uint.MaxValue - 1;

    private void WriteUInt16(ushort value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        _output.Write(bytes);
    }

    private void WriteUInt32(uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        _output.Write(bytes);
    }

    private void WriteUInt64(ulong value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        _output.Write(bytes);
    }

    private sealed record CentralDirectoryEntry(
        byte[] NameBytes,
        ushort CompressionMethod,
        uint Crc32,
        long CompressedSize,
        long UncompressedSize,
        long LocalHeaderOffset,
        bool HasDataDescriptor);

    private sealed class StoredEntryStream(Stream output) : Stream
    {
        private readonly Stream _output = output;
        private readonly Crc32Calculator _crc32 = new();

        public long BytesWritten { get; private set; }

        public uint Crc32 => _crc32.Value;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _output.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _output.Write(buffer);
            _crc32.Append(buffer);
            BytesWritten += buffer.Length;
        }

        public override void WriteByte(byte value)
        {
            _output.WriteByte(value);
            _crc32.Append([value]);
            BytesWritten++;
        }
    }
}

/// <summary>
/// Incremental ZIP CRC-32 (reflected, polynomial <c>0xEDB88320</c>). Delegates to
/// <see cref="System.IO.Hashing.Crc32"/>, which uses the CPU's hardware CRC instructions
/// (Arm64 <c>crc32*</c>, x64 SSE4.2) where available and falls back to a portable software path.
/// The produced value is byte-identical to the previous scalar table implementation and to the
/// standard ZIP/PKZIP CRC-32, verified by authoring round-trip and byte-identical output tests.
/// </summary>
internal sealed class Crc32Calculator
{
    private readonly System.IO.Hashing.Crc32 _crc = new();

    public uint Value => _crc.GetCurrentHashAsUInt32();

    public void Append(ReadOnlySpan<byte> bytes) => _crc.Append(bytes);
}

internal sealed record StoredZipEntryInfo(int LocalHeaderSize, long ContentOffset, long UncompressedSize);

internal sealed record DeflatedZipEntryContent(uint Crc32, long CompressedSize, long UncompressedSize);
