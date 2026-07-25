using System.Buffers.Binary;
using System.Text;

namespace MsixCore.Packaging.Authoring;

internal sealed class StoredZipWriter : IDisposable
{
    private const uint LocalFileHeaderSignature = 0x04034B50;
    private const uint CentralDirectoryHeaderSignature = 0x02014B50;
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const ushort Version20 = 20;
    private const ushort Utf8Flag = 0x0800;
    private const ushort StoredMethod = 0;
    private const ushort DeflateMethod = 8;
    private const ushort DosTime = 0;
    private const ushort DosDate = 0x0021;
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

        if (_entries.Count >= ushort.MaxValue)
        {
            throw new NotSupportedException("ZIP archives with more than 65535 entries require ZIP64.");
        }

        byte[] nameBytes = Utf8.GetBytes(entryName);
        int localHeaderSize = 30 + nameBytes.Length;
        if (nameBytes.Length > ushort.MaxValue || localHeaderSize > ushort.MaxValue)
        {
            throw new InvalidDataException($"The ZIP entry name '{entryName}' is too long.");
        }

        uint localHeaderOffset = ToUInt32(_output.Position, "ZIP local-header offset");
        WriteUInt32(LocalFileHeaderSignature);
        WriteUInt16(Version20);
        WriteUInt16(Utf8Flag);
        WriteUInt16(StoredMethod);
        WriteUInt16(DosTime);
        WriteUInt16(DosDate);
        WriteUInt32(0);
        WriteUInt32(0);
        WriteUInt32(0);
        WriteUInt16((ushort)nameBytes.Length);
        WriteUInt16(0);
        _output.Write(nameBytes);

        var entryStream = new StoredEntryStream(_output);
        writeContent(entryStream);
        uint size = ToUInt32(entryStream.BytesWritten, $"ZIP entry '{entryName}' size");
        uint crc32 = entryStream.Crc32;

        long endPosition = _output.Position;
        _output.Position = localHeaderOffset + 14;
        WriteUInt32(crc32);
        WriteUInt32(size);
        WriteUInt32(size);
        _output.Position = endPosition;

        _entries.Add(new CentralDirectoryEntry(
            nameBytes,
            StoredMethod,
            crc32,
            size,
            size,
            localHeaderOffset));
        return new StoredZipEntryInfo(localHeaderSize);
    }

    public StoredZipEntryInfo AddDeflatedEntry(
        string entryName,
        Func<Stream, DeflatedZipEntryContent> writeContent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(entryName);
        ArgumentNullException.ThrowIfNull(writeContent);

        if (_entries.Count >= ushort.MaxValue)
        {
            throw new NotSupportedException("ZIP archives with more than 65535 entries require ZIP64.");
        }

        byte[] nameBytes = Utf8.GetBytes(entryName);
        int localHeaderSize = 30 + nameBytes.Length;
        if (nameBytes.Length > ushort.MaxValue || localHeaderSize > ushort.MaxValue)
        {
            throw new InvalidDataException($"The ZIP entry name '{entryName}' is too long.");
        }

        uint localHeaderOffset = ToUInt32(_output.Position, "ZIP local-header offset");
        WriteUInt32(LocalFileHeaderSignature);
        WriteUInt16(Version20);
        WriteUInt16(Utf8Flag);
        WriteUInt16(DeflateMethod);
        WriteUInt16(DosTime);
        WriteUInt16(DosDate);
        WriteUInt32(0);
        WriteUInt32(0);
        WriteUInt32(0);
        WriteUInt16((ushort)nameBytes.Length);
        WriteUInt16(0);
        _output.Write(nameBytes);

        long contentOffset = _output.Position;
        DeflatedZipEntryContent content = writeContent(_output);
        uint actualCompressedSize = ToUInt32(
            _output.Position - contentOffset,
            $"ZIP entry '{entryName}' compressed size");
        if (actualCompressedSize != content.CompressedSize)
        {
            throw new InvalidDataException(
                $"ZIP entry '{entryName}' wrote {actualCompressedSize} compressed bytes but reported {content.CompressedSize}.");
        }

        long endPosition = _output.Position;
        _output.Position = localHeaderOffset + 14;
        WriteUInt32(content.Crc32);
        WriteUInt32(content.CompressedSize);
        WriteUInt32(content.UncompressedSize);
        _output.Position = endPosition;

        _entries.Add(new CentralDirectoryEntry(
            nameBytes,
            DeflateMethod,
            content.Crc32,
            content.CompressedSize,
            content.UncompressedSize,
            localHeaderOffset));
        return new StoredZipEntryInfo(localHeaderSize);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        uint centralDirectoryOffset = ToUInt32(_output.Position, "ZIP central-directory offset");
        foreach (CentralDirectoryEntry entry in _entries)
        {
            WriteUInt32(CentralDirectoryHeaderSignature);
            WriteUInt16(Version20);
            WriteUInt16(Version20);
            WriteUInt16(Utf8Flag);
            WriteUInt16(entry.CompressionMethod);
            WriteUInt16(DosTime);
            WriteUInt16(DosDate);
            WriteUInt32(entry.Crc32);
            WriteUInt32(entry.CompressedSize);
            WriteUInt32(entry.UncompressedSize);
            WriteUInt16((ushort)entry.NameBytes.Length);
            WriteUInt16(0);
            WriteUInt16(0);
            WriteUInt16(0);
            WriteUInt16(0);
            WriteUInt32(0);
            WriteUInt32(entry.LocalHeaderOffset);
            _output.Write(entry.NameBytes);
        }

        uint centralDirectorySize = ToUInt32(
            _output.Position - centralDirectoryOffset,
            "ZIP central-directory size");
        ushort entryCount = (ushort)_entries.Count;
        WriteUInt32(EndOfCentralDirectorySignature);
        WriteUInt16(0);
        WriteUInt16(0);
        WriteUInt16(entryCount);
        WriteUInt16(entryCount);
        WriteUInt32(centralDirectorySize);
        WriteUInt32(centralDirectoryOffset);
        WriteUInt16(0);
        _output.Flush();
        _disposed = true;
    }

    private static uint ToUInt32(long value, string description)
    {
        if (value < 0 || value > uint.MaxValue)
        {
            throw new NotSupportedException($"{description} exceeds the non-ZIP64 limit.");
        }

        return (uint)value;
    }

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

    private sealed record CentralDirectoryEntry(
        byte[] NameBytes,
        ushort CompressionMethod,
        uint Crc32,
        uint CompressedSize,
        uint UncompressedSize,
        uint LocalHeaderOffset);

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

internal sealed class Crc32Calculator
{
    private static readonly uint[] Table = CreateTable();
    private uint _state = uint.MaxValue;

    public uint Value => ~_state;

    public void Append(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            _state = Table[(byte)(_state ^ value)] ^ (_state >> 8);
        }
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value >> 1) ^ ((value & 1) == 0 ? 0 : 0xEDB88320);
            }

            table[i] = value;
        }

        return table;
    }
}

internal sealed record StoredZipEntryInfo(int LocalHeaderSize);

internal sealed record DeflatedZipEntryContent(uint Crc32, uint CompressedSize, uint UncompressedSize);
