using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace MsixCore.CorpusRoundtrip;

/// <summary>Central-directory ZIP entry metadata used by the structural differ.</summary>
public sealed record ZipEntryInfo(
    int Index,
    string Name,
    ushort CompressionMethod,
    long CompressedSize,
    long UncompressedSize,
    uint Crc32,
    long LocalHeaderOffset);

/// <summary>A human-readable ZIP structural difference.</summary>
public sealed record ZipStructuralDifference(string EntryName, string Field, string Left, string Right, string Interpretation);

/// <summary>Result of comparing two ZIP central directories plus raw bytes.</summary>
public sealed record ZipStructuralDiffResult(
    bool IsIdentical,
    long? FirstByteDifference,
    IReadOnlyList<ZipStructuralDifference> Differences);

/// <summary>Parses ZIP central directories and reports structural package differences.</summary>
public sealed class ZipStructuralDiffer
{
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const uint CentralDirectoryHeaderSignature = 0x02014B50;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    /// <summary>Compares two archive files.</summary>
    public static ZipStructuralDiffResult Compare(string leftPath, string rightPath)
    {
        IReadOnlyList<ZipEntryInfo> left = ReadEntries(leftPath);
        IReadOnlyList<ZipEntryInfo> right = ReadEntries(rightPath);
        var differences = new List<ZipStructuralDifference>();

        int sharedCount = Math.Min(left.Count, right.Count);
        for (int i = 0; i < sharedCount; i++)
        {
            ZipEntryInfo leftEntry = left[i];
            ZipEntryInfo rightEntry = right[i];
            string entryName = leftEntry.Name == rightEntry.Name
                ? leftEntry.Name
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{leftEntry.Name} | {rightEntry.Name}");

            AddIfDifferent(differences, entryName, "entry name", leftEntry.Name, rightEntry.Name, "Central-directory order or OPC name encoding differs.");
            AddIfDifferent(
                differences,
                entryName,
                "compression method",
                leftEntry.CompressionMethod.ToString(CultureInfo.InvariantCulture),
                rightEntry.CompressionMethod.ToString(CultureInfo.InvariantCulture),
                "Stored should be method 0; optimal payloads may use method 8 except pre-compressed extensions.");
            AddIfDifferent(
                differences,
                entryName,
                "compressed size",
                leftEntry.CompressedSize.ToString(CultureInfo.InvariantCulture),
                rightEntry.CompressedSize.ToString(CultureInfo.InvariantCulture),
                "Entry bytes differ after compression or header accounting differs.");
            AddIfDifferent(
                differences,
                entryName,
                "uncompressed size",
                leftEntry.UncompressedSize.ToString(CultureInfo.InvariantCulture),
                rightEntry.UncompressedSize.ToString(CultureInfo.InvariantCulture),
                "The two packages do not contain the same logical entry bytes.");
            AddIfDifferent(
                differences,
                entryName,
                "CRC-32",
                leftEntry.Crc32.ToString("X8", CultureInfo.InvariantCulture),
                rightEntry.Crc32.ToString("X8", CultureInfo.InvariantCulture),
                "The entry payload bytes differ.");
            AddIfDifferent(
                differences,
                entryName,
                "local-header offset",
                leftEntry.LocalHeaderOffset.ToString(CultureInfo.InvariantCulture),
                rightEntry.LocalHeaderOffset.ToString(CultureInfo.InvariantCulture),
                "Earlier entry sizes, ordering, or ZIP header fields differ.");
        }

        if (left.Count != right.Count)
        {
            differences.Add(new ZipStructuralDifference(
                "<central-directory>",
                "entry count",
                left.Count.ToString(CultureInfo.InvariantCulture),
                right.Count.ToString(CultureInfo.InvariantCulture),
                "One package has extra or missing ZIP entries."));
        }

        AddPresenceDifferences(differences, left, right);
        long? firstByteDifference = RawByteDiffer.FindFirstDifference(leftPath, rightPath);
        return new ZipStructuralDiffResult(firstByteDifference is null && differences.Count == 0, firstByteDifference, differences);
    }

    /// <summary>Reads the ordered central-directory entries from <paramref name="path"/>.</summary>
    public static IReadOnlyList<ZipEntryInfo> ReadEntries(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int eocd = FindEndOfCentralDirectory(bytes);
        long entryCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(eocd + 10, 2));
        long centralDirectoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(eocd + 16, 4));
        if (entryCount == ushort.MaxValue || centralDirectoryOffset == uint.MaxValue)
        {
            (entryCount, centralDirectoryOffset) = ReadZip64EndOfCentralDirectory(bytes, eocd);
        }

        int position = checked((int)centralDirectoryOffset);
        var entries = new List<ZipEntryInfo>(checked((int)entryCount));

        for (int index = 0; index < entryCount; index++)
        {
            uint signature = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position, 4));
            if (signature != CentralDirectoryHeaderSignature)
            {
                throw new InvalidDataException("The ZIP central directory is malformed.");
            }

            ushort method = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position + 10, 2));
            uint crc32 = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 16, 4));
            long compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 20, 4));
            long uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 24, 4));
            ushort fileNameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position + 28, 2));
            ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position + 30, 2));
            ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position + 32, 2));
            long localHeaderOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 42, 4));
            string name = Utf8.GetString(bytes.AsSpan(position + 46, fileNameLength));
            ApplyZip64Extra(
                bytes.AsSpan(position + 46 + fileNameLength, extraLength),
                ref compressedSize,
                ref uncompressedSize,
                ref localHeaderOffset);
            entries.Add(new ZipEntryInfo(index, name, method, compressedSize, uncompressedSize, crc32, localHeaderOffset));
            position += 46 + fileNameLength + extraLength + commentLength;
        }

        return entries;
    }

    private static int FindEndOfCentralDirectory(byte[] bytes)
    {
        int minimum = Math.Max(0, bytes.Length - ushort.MaxValue - 22);
        for (int offset = bytes.Length - 22; offset >= minimum; offset--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)) == EndOfCentralDirectorySignature)
            {
                return offset;
            }
        }

        throw new InvalidDataException("The ZIP end-of-central-directory record was not found.");
    }

    private static (long EntryCount, long CentralDirectoryOffset) ReadZip64EndOfCentralDirectory(byte[] bytes, int eocd)
    {
        const uint Zip64LocatorSignature = 0x07064B50;
        const uint Zip64EndOfCentralDirectorySignature = 0x06064B50;
        int locator = eocd - 20;
        if (locator < 0 || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(locator, 4)) != Zip64LocatorSignature)
        {
            throw new NotSupportedException("ZIP64 central-directory locator was not found.");
        }

        ulong zip64Offset = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(locator + 8, 8));
        int zip64 = checked((int)zip64Offset);
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(zip64, 4)) != Zip64EndOfCentralDirectorySignature)
        {
            throw new InvalidDataException("The ZIP64 end-of-central-directory record is malformed.");
        }

        ulong entryCount = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(zip64 + 32, 8));
        ulong centralDirectoryOffset = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(zip64 + 48, 8));
        return (checked((long)entryCount), checked((long)centralDirectoryOffset));
    }

    private static void ApplyZip64Extra(
        ReadOnlySpan<byte> extra,
        ref long compressedSize,
        ref long uncompressedSize,
        ref long localHeaderOffset)
    {
        int position = 0;
        while (position + 4 <= extra.Length)
        {
            ushort headerId = BinaryPrimitives.ReadUInt16LittleEndian(extra.Slice(position, 2));
            ushort dataSize = BinaryPrimitives.ReadUInt16LittleEndian(extra.Slice(position + 2, 2));
            position += 4;
            if (position + dataSize > extra.Length)
            {
                throw new InvalidDataException("The ZIP extra field is malformed.");
            }

            if (headerId == 0x0001)
            {
                ReadOnlySpan<byte> zip64 = extra.Slice(position, dataSize);
                int zip64Position = 0;
                if (uncompressedSize == uint.MaxValue)
                {
                    uncompressedSize = ReadZip64Value(zip64, ref zip64Position);
                }

                if (compressedSize == uint.MaxValue)
                {
                    compressedSize = ReadZip64Value(zip64, ref zip64Position);
                }

                if (localHeaderOffset == uint.MaxValue)
                {
                    localHeaderOffset = ReadZip64Value(zip64, ref zip64Position);
                }
            }

            position += dataSize;
        }
    }

    private static long ReadZip64Value(ReadOnlySpan<byte> zip64, ref int position)
    {
        if (position + sizeof(ulong) > zip64.Length)
        {
            throw new InvalidDataException("The ZIP64 extended information field is truncated.");
        }

        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(zip64.Slice(position, sizeof(ulong)));
        position += sizeof(ulong);
        return checked((long)value);
    }

    private static void AddIfDifferent(
        List<ZipStructuralDifference> differences,
        string entryName,
        string field,
        string left,
        string right,
        string interpretation)
    {
        if (!string.Equals(left, right, StringComparison.Ordinal))
        {
            differences.Add(new ZipStructuralDifference(entryName, field, left, right, interpretation));
        }
    }

    private static void AddPresenceDifferences(
        List<ZipStructuralDifference> differences,
        IReadOnlyList<ZipEntryInfo> left,
        IReadOnlyList<ZipEntryInfo> right)
    {
        var leftNames = left.Select(static entry => entry.Name).ToHashSet(StringComparer.Ordinal);
        var rightNames = right.Select(static entry => entry.Name).ToHashSet(StringComparer.Ordinal);
        foreach (string name in leftNames.Except(rightNames, StringComparer.Ordinal).OrderBy(static name => name, StringComparer.Ordinal))
        {
            differences.Add(new ZipStructuralDifference(name, "presence", "present", "missing", "The entry is absent from the right package."));
        }

        foreach (string name in rightNames.Except(leftNames, StringComparer.Ordinal).OrderBy(static name => name, StringComparer.Ordinal))
        {
            differences.Add(new ZipStructuralDifference(name, "presence", "missing", "present", "The entry is absent from the left package."));
        }
    }
}
