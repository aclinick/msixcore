using System.Buffers.Binary;
using System.Globalization;

namespace MsixCore.CorpusRoundtrip;

internal static class VariantZipRewriter
{
    private const uint LocalHeaderSignature = 0x04034B50;
    private const uint CentralHeaderSignature = 0x02014B50;
    private const uint EocdSignature = 0x06054B50;
    private const uint Zip64EocdSignature = 0x06064B50;
    private const uint Zip64LocatorSignature = 0x07064B50;
    private const uint DataDescriptorSignature = 0x08074B50;
    private const ushort Version20 = 20;
    private const ushort Version45 = 45;
    private const ushort Utf8Flag = 0x0800;
    private const ushort DataDescriptorFlag = 0x0008;

    public static int WriteVariant(string inputPath, string outputPath, string variant)
    {
        bool zip64 = variant.Equals("zip64", StringComparison.OrdinalIgnoreCase)
            || variant.Equals("both", StringComparison.OrdinalIgnoreCase);
        bool descriptor = variant.Equals("descriptor", StringComparison.OrdinalIgnoreCase)
            || variant.Equals("both", StringComparison.OrdinalIgnoreCase);
        bool clearUtf8 = variant.Equals("no-utf8", StringComparison.OrdinalIgnoreCase);
        if (!zip64 && !descriptor && !clearUtf8 && !variant.Equals("baseline", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unknown variant '{variant}'.", nameof(variant));
        }

        byte[] input = File.ReadAllBytes(inputPath);
        List<Entry> entries = ReadEntries(input);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        using FileStream output = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var written = new List<WrittenEntry>(entries.Count);
        foreach (Entry entry in entries)
        {
            long localOffset = output.Position;
            ushort flags = (ushort)(entry.Flags & ~DataDescriptorFlag);
            if (descriptor)
            {
                flags |= DataDescriptorFlag;
            }

            if (clearUtf8)
            {
                flags = (ushort)(flags & ~Utf8Flag);
            }

            WriteUInt32(output, LocalHeaderSignature);
            WriteUInt16(output, zip64 ? Version45 : Version20);
            WriteUInt16(output, flags);
            WriteUInt16(output, entry.Method);
            WriteUInt16(output, entry.LastModTime);
            WriteUInt16(output, entry.LastModDate);
            WriteUInt32(output, descriptor ? 0u : entry.Crc32);
            WriteUInt32(output, descriptor ? 0u : entry.CompressedSize);
            WriteUInt32(output, descriptor ? 0u : entry.UncompressedSize);
            WriteUInt16(output, checked((ushort)entry.NameBytes.Length));
            WriteUInt16(output, 0);
            output.Write(entry.NameBytes);
            output.Write(entry.CompressedData);
            if (descriptor)
            {
                WriteUInt32(output, DataDescriptorSignature);
                WriteUInt32(output, entry.Crc32);
                if (zip64)
                {
                    WriteUInt64(output, entry.CompressedSize);
                    WriteUInt64(output, entry.UncompressedSize);
                }
                else
                {
                    WriteUInt32(output, entry.CompressedSize);
                    WriteUInt32(output, entry.UncompressedSize);
                }
            }

            written.Add(new WrittenEntry(entry, flags, localOffset));
        }

        long centralOffset = output.Position;
        foreach (WrittenEntry writtenEntry in written)
        {
            Entry entry = writtenEntry.Entry;
            WriteUInt32(output, CentralHeaderSignature);
            WriteUInt16(output, zip64 ? Version45 : Version20);
            WriteUInt16(output, zip64 ? Version45 : Version20);
            WriteUInt16(output, writtenEntry.Flags);
            WriteUInt16(output, entry.Method);
            WriteUInt16(output, entry.LastModTime);
            WriteUInt16(output, entry.LastModDate);
            WriteUInt32(output, entry.Crc32);
            WriteUInt32(output, zip64 ? uint.MaxValue : entry.CompressedSize);
            WriteUInt32(output, zip64 ? uint.MaxValue : entry.UncompressedSize);
            WriteUInt16(output, checked((ushort)entry.NameBytes.Length));
            WriteUInt16(output, zip64 ? (ushort)28 : (ushort)0);
            WriteUInt16(output, 0);
            WriteUInt16(output, 0);
            WriteUInt16(output, 0);
            WriteUInt32(output, 0);
            WriteUInt32(output, zip64 ? uint.MaxValue : checked((uint)writtenEntry.LocalHeaderOffset));
            output.Write(entry.NameBytes);
            if (zip64)
            {
                WriteUInt16(output, 0x0001);
                WriteUInt16(output, 24);
                WriteUInt64(output, entry.UncompressedSize);
                WriteUInt64(output, entry.CompressedSize);
                WriteUInt64(output, (ulong)writtenEntry.LocalHeaderOffset);
            }
        }

        long centralSize = output.Position - centralOffset;
        if (zip64)
        {
            long zip64EocdOffset = output.Position;
            WriteUInt32(output, Zip64EocdSignature);
            WriteUInt64(output, 44);
            WriteUInt16(output, Version45);
            WriteUInt16(output, Version45);
            WriteUInt32(output, 0);
            WriteUInt32(output, 0);
            WriteUInt64(output, (ulong)written.Count);
            WriteUInt64(output, (ulong)written.Count);
            WriteUInt64(output, (ulong)centralSize);
            WriteUInt64(output, (ulong)centralOffset);

            WriteUInt32(output, Zip64LocatorSignature);
            WriteUInt32(output, 0);
            WriteUInt64(output, (ulong)zip64EocdOffset);
            WriteUInt32(output, 1);
        }

        WriteUInt32(output, EocdSignature);
        WriteUInt16(output, 0);
        WriteUInt16(output, 0);
        WriteUInt16(output, zip64 ? ushort.MaxValue : checked((ushort)written.Count));
        WriteUInt16(output, zip64 ? ushort.MaxValue : checked((ushort)written.Count));
        WriteUInt32(output, zip64 ? uint.MaxValue : checked((uint)centralSize));
        WriteUInt32(output, zip64 ? uint.MaxValue : checked((uint)centralOffset));
        WriteUInt16(output, 0);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Wrote {variant}: {outputPath} ({written.Count} entries)"));
        return 0;
    }

    private static List<Entry> ReadEntries(byte[] archive)
    {
        int eocd = FindEocd(archive);
        long centralSize;
        long centralOffset;

        uint centralSize32 = ReadUInt32(archive, eocd + 12);
        uint centralOffset32 = ReadUInt32(archive, eocd + 16);

        if (centralSize32 == uint.MaxValue || centralOffset32 == uint.MaxValue
            || ReadUInt16(archive, eocd + 8) == ushort.MaxValue
            || ReadUInt16(archive, eocd + 10) == ushort.MaxValue)
        {
            // Classic EOCD has sentinel values — resolve from ZIP64 EOCD.
            (centralSize, centralOffset) = ReadZip64CentralDirectory(archive, eocd);
        }
        else
        {
            centralSize = centralSize32;
            centralOffset = centralOffset32;
        }

        long position = centralOffset;
        long centralEnd = position + centralSize;
        var entries = new List<Entry>();
        while (position < centralEnd)
        {
            int pos = checked((int)position);
            if (ReadUInt32(archive, pos) != CentralHeaderSignature)
            {
                throw new InvalidDataException($"Expected central directory header at 0x{pos:X}.");
            }

            ushort flags = ReadUInt16(archive, pos + 8);
            ushort method = ReadUInt16(archive, pos + 10);
            ushort lastModTime = ReadUInt16(archive, pos + 12);
            ushort lastModDate = ReadUInt16(archive, pos + 14);
            uint crc32 = ReadUInt32(archive, pos + 16);
            uint compressedSize32 = ReadUInt32(archive, pos + 20);
            uint uncompressedSize32 = ReadUInt32(archive, pos + 24);
            ushort nameLength = ReadUInt16(archive, pos + 28);
            ushort extraLength = ReadUInt16(archive, pos + 30);
            ushort commentLength = ReadUInt16(archive, pos + 32);
            uint localOffset32 = ReadUInt32(archive, pos + 42);
            byte[] nameBytes = archive.AsSpan(pos + 46, nameLength).ToArray();

            // Resolve ZIP64 extra field if sentinel values present.
            long compressedSize = compressedSize32;
            long uncompressedSize = uncompressedSize32;
            long localOffset = localOffset32;
            if (extraLength > 0)
            {
                ResolveZip64Extra(
                    archive.AsSpan(pos + 46 + nameLength, extraLength),
                    compressedSize32, uncompressedSize32, localOffset32,
                    ref compressedSize, ref uncompressedSize, ref localOffset);
            }

            int local = checked((int)localOffset);
            if (ReadUInt32(archive, local) != LocalHeaderSignature)
            {
                throw new InvalidDataException($"Expected local header for central entry at 0x{local:X}.");
            }

            ushort localNameLength = ReadUInt16(archive, local + 26);
            ushort localExtraLength = ReadUInt16(archive, local + 28);
            int dataOffset = local + 30 + localNameLength + localExtraLength;
            byte[] compressedData = archive.AsSpan(dataOffset, checked((int)compressedSize)).ToArray();
            entries.Add(new Entry(
                nameBytes,
                flags,
                method,
                lastModTime,
                lastModDate,
                crc32,
                checked((uint)compressedSize),
                checked((uint)uncompressedSize),
                compressedData));
            position += 46 + nameLength + extraLength + commentLength;
        }

        return entries;
    }

    private static (long CentralSize, long CentralOffset) ReadZip64CentralDirectory(byte[] archive, int eocd)
    {
        if (eocd < 20)
        {
            throw new InvalidDataException("ZIP64 EOCD locator not found.");
        }

        int locatorOffset = eocd - 20;
        if (ReadUInt32(archive, locatorOffset) != Zip64LocatorSignature)
        {
            throw new InvalidDataException("ZIP64 EOCD locator not found.");
        }

        long zip64EocdOffset = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(
            archive.AsSpan(locatorOffset + 8, 8)));
        int z64 = checked((int)zip64EocdOffset);
        if (ReadUInt32(archive, z64) != Zip64EocdSignature)
        {
            throw new InvalidDataException("ZIP64 EOCD record is malformed.");
        }

        long centralSize = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(archive.AsSpan(z64 + 40, 8)));
        long centralOffset = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(archive.AsSpan(z64 + 48, 8)));
        return (centralSize, centralOffset);
    }

    private static void ResolveZip64Extra(
        ReadOnlySpan<byte> extra,
        uint compressedSize32,
        uint uncompressedSize32,
        uint localOffset32,
        ref long compressedSize,
        ref long uncompressedSize,
        ref long localOffset)
    {
        int pos = 0;
        while (pos + 4 <= extra.Length)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(extra.Slice(pos, 2));
            ushort size = BinaryPrimitives.ReadUInt16LittleEndian(extra.Slice(pos + 2, 2));
            if (id == 0x0001 && pos + 4 + size <= extra.Length)
            {
                int fieldPos = pos + 4;
                if (uncompressedSize32 == uint.MaxValue && fieldPos + 8 <= pos + 4 + size)
                {
                    uncompressedSize = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(extra.Slice(fieldPos, 8)));
                    fieldPos += 8;
                }
                if (compressedSize32 == uint.MaxValue && fieldPos + 8 <= pos + 4 + size)
                {
                    compressedSize = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(extra.Slice(fieldPos, 8)));
                    fieldPos += 8;
                }
                if (localOffset32 == uint.MaxValue && fieldPos + 8 <= pos + 4 + size)
                {
                    localOffset = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(extra.Slice(fieldPos, 8)));
                }
                return;
            }
            pos += 4 + size;
        }
    }

    private static int FindEocd(byte[] archive)
    {
        int minimum = Math.Max(0, archive.Length - ushort.MaxValue - 22);
        for (int i = archive.Length - 22; i >= minimum; i--)
        {
            if (ReadUInt32(archive, i) == EocdSignature)
            {
                return i;
            }
        }

        throw new InvalidDataException("EOCD was not found.");
    }

    private static ushort ReadUInt16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));

    private static uint ReadUInt32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt64(Stream stream, ulong value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private sealed record Entry(
        byte[] NameBytes,
        ushort Flags,
        ushort Method,
        ushort LastModTime,
        ushort LastModDate,
        uint Crc32,
        uint CompressedSize,
        uint UncompressedSize,
        byte[] CompressedData);

    private sealed record WrittenEntry(Entry Entry, ushort Flags, long LocalHeaderOffset);
}
