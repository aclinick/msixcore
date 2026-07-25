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
                WriteUInt64(output, entry.CompressedSize);
                WriteUInt64(output, entry.UncompressedSize);
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
        uint centralSize = ReadUInt32(archive, eocd + 12);
        uint centralOffset = ReadUInt32(archive, eocd + 16);
        int position = checked((int)centralOffset);
        int centralEnd = checked(position + (int)centralSize);
        var entries = new List<Entry>();
        while (position < centralEnd)
        {
            if (ReadUInt32(archive, position) != CentralHeaderSignature)
            {
                throw new InvalidDataException($"Expected central directory header at 0x{position:X}.");
            }

            ushort flags = ReadUInt16(archive, position + 8);
            ushort method = ReadUInt16(archive, position + 10);
            ushort lastModTime = ReadUInt16(archive, position + 12);
            ushort lastModDate = ReadUInt16(archive, position + 14);
            uint crc32 = ReadUInt32(archive, position + 16);
            uint compressedSize = ReadUInt32(archive, position + 20);
            uint uncompressedSize = ReadUInt32(archive, position + 24);
            ushort nameLength = ReadUInt16(archive, position + 28);
            ushort extraLength = ReadUInt16(archive, position + 30);
            ushort commentLength = ReadUInt16(archive, position + 32);
            uint localOffset = ReadUInt32(archive, position + 42);
            byte[] nameBytes = archive.AsSpan(position + 46, nameLength).ToArray();

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
                compressedSize,
                uncompressedSize,
                compressedData));
            position += 46 + nameLength + extraLength + commentLength;
        }

        return entries;
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

