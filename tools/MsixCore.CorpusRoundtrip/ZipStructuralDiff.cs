using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace MsixCore.CorpusRoundtrip;

/// <summary>ZIP entry metadata used by the structural differ.</summary>
public sealed record ZipEntryInfo(
    int Index,
    string Name,
    ushort VersionNeeded,
    ushort GeneralPurposeFlags,
    ushort CompressionMethod,
    long CompressedSize,
    long UncompressedSize,
    uint Crc32,
    long LocalHeaderOffset,
    ushort CentralDirectoryExtraLength,
    string CentralDirectoryExtraFields,
    ushort LocalHeaderVersionNeeded,
    ushort LocalHeaderGeneralPurposeFlags,
    ushort LocalHeaderCompressionMethod,
    uint LocalHeaderCrc32,
    uint LocalHeaderCompressedSize32,
    uint LocalHeaderUncompressedSize32,
    ushort LocalHeaderExtraLength,
    string LocalHeaderExtraFields);

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
    private const uint LocalFileHeaderSignature = 0x04034B50;
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const uint CentralDirectoryHeaderSignature = 0x02014B50;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    /// <summary>Compares two archive files.</summary>
    public static ZipStructuralDiffResult Compare(string leftPath, string rightPath)
    {
        IReadOnlyList<ZipEntryInfo> left = ReadEntries(leftPath);
        IReadOnlyList<ZipEntryInfo> right = ReadEntries(rightPath);
        var differences = new List<ZipStructuralDifference>();

        CompareSharedEntries(differences, left, right);
        CompareOrdering(differences, left, right);

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
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 128 * 1024, FileOptions.SequentialScan);
        (long entryCount, long centralDirectoryOffset) = FindCentralDirectory(stream);
        stream.Position = centralDirectoryOffset;
        var entries = new List<ZipEntryInfo>(checked((int)entryCount));
        byte[] fixedHeaderBuffer = new byte[46];

        for (int index = 0; index < entryCount; index++)
        {
            Span<byte> fixedHeader = fixedHeaderBuffer;
            ReadExactly(stream, fixedHeader);
            uint signature = BinaryPrimitives.ReadUInt32LittleEndian(fixedHeader[..4]);
            if (signature != CentralDirectoryHeaderSignature)
            {
                throw new InvalidDataException("The ZIP central directory is malformed.");
            }

            ushort versionNeeded = BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.Slice(6, 2));
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.Slice(8, 2));
            ushort method = BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.Slice(10, 2));
            uint crc32 = BinaryPrimitives.ReadUInt32LittleEndian(fixedHeader.Slice(16, 4));
            uint compressedSize32 = BinaryPrimitives.ReadUInt32LittleEndian(fixedHeader.Slice(20, 4));
            uint uncompressedSize32 = BinaryPrimitives.ReadUInt32LittleEndian(fixedHeader.Slice(24, 4));
            long compressedSize = compressedSize32;
            long uncompressedSize = uncompressedSize32;
            ushort fileNameLength = BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.Slice(28, 2));
            ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.Slice(30, 2));
            ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.Slice(32, 2));
            uint localHeaderOffset32 = BinaryPrimitives.ReadUInt32LittleEndian(fixedHeader.Slice(42, 4));
            long localHeaderOffset = localHeaderOffset32;

            byte[] nameBytes = ReadBytes(stream, fileNameLength);
            byte[] extra = ReadBytes(stream, extraLength);
            _ = ReadBytes(stream, commentLength);
            string name = Utf8.GetString(nameBytes);
            ApplyZip64Extra(extra, compressedSize32, uncompressedSize32, localHeaderOffset32, ref compressedSize, ref uncompressedSize, ref localHeaderOffset);
            LocalHeaderInfo local = ReadLocalHeader(stream, localHeaderOffset);
            entries.Add(new ZipEntryInfo(
                index,
                name,
                versionNeeded,
                flags,
                method,
                compressedSize,
                uncompressedSize,
                crc32,
                localHeaderOffset,
                extraLength,
                FormatExtraFields(extra),
                local.VersionNeeded,
                local.GeneralPurposeFlags,
                local.CompressionMethod,
                local.Crc32,
                local.CompressedSize32,
                local.UncompressedSize32,
                local.ExtraLength,
                FormatExtraFields(local.Extra)));
        }

        return entries;
    }

    private static void CompareOrdering(
        List<ZipStructuralDifference> differences,
        IReadOnlyList<ZipEntryInfo> left,
        IReadOnlyList<ZipEntryInfo> right)
    {
        int sharedCount = Math.Min(left.Count, right.Count);
        for (int i = 0; i < sharedCount; i++)
        {
            if (!string.Equals(left[i].Name, right[i].Name, StringComparison.Ordinal))
            {
                differences.Add(new ZipStructuralDifference(
                    "<central-directory>",
                    "entry order[" + i.ToString(CultureInfo.InvariantCulture) + "]",
                    left[i].Name,
                    right[i].Name,
                    "Central-directory entry ordering differs."));
            }
        }
    }

    private static void CompareSharedEntries(
        List<ZipStructuralDifference> differences,
        IReadOnlyList<ZipEntryInfo> left,
        IReadOnlyList<ZipEntryInfo> right)
    {
        Dictionary<string, ZipEntryInfo> leftByName = left.ToDictionary(static entry => entry.Name, StringComparer.Ordinal);
        Dictionary<string, ZipEntryInfo> rightByName = right.ToDictionary(static entry => entry.Name, StringComparer.Ordinal);
        foreach (string name in leftByName.Keys.Intersect(rightByName.Keys, StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal))
        {
            ZipEntryInfo leftEntry = leftByName[name];
            ZipEntryInfo rightEntry = rightByName[name];
            CompareEntry(differences, name, leftEntry, rightEntry);
        }
    }

    private static void CompareEntry(List<ZipStructuralDifference> differences, string name, ZipEntryInfo left, ZipEntryInfo right)
    {
        AddIfDifferent(differences, name, "central-directory index", left.Index.ToString(CultureInfo.InvariantCulture), right.Index.ToString(CultureInfo.InvariantCulture), "Central-directory entry ordering differs.");
        AddIfDifferent(differences, name, "central-directory version-needed-to-extract", Version(left.VersionNeeded), Version(right.VersionNeeded), "ZIP version-needed metadata differs; 4.5 indicates ZIP64.");
        AddIfDifferent(differences, name, "central-directory general-purpose flags", Flags(left.GeneralPurposeFlags), Flags(right.GeneralPurposeFlags), "General-purpose bit flags differ, including UTF-8 or data-descriptor bits.");
        AddIfDifferent(differences, name, "compression method", left.CompressionMethod.ToString(CultureInfo.InvariantCulture), right.CompressionMethod.ToString(CultureInfo.InvariantCulture), "Stored should be method 0; optimal payloads may use method 8 except pre-compressed extensions.");
        AddIfDifferent(differences, name, "compressed size", left.CompressedSize.ToString(CultureInfo.InvariantCulture), right.CompressedSize.ToString(CultureInfo.InvariantCulture), "Entry bytes differ after compression or ZIP64 size decoding differs.");
        AddIfDifferent(differences, name, "uncompressed size", left.UncompressedSize.ToString(CultureInfo.InvariantCulture), right.UncompressedSize.ToString(CultureInfo.InvariantCulture), "The two packages do not contain the same logical entry bytes.");
        AddIfDifferent(differences, name, "CRC-32", left.Crc32.ToString("X8", CultureInfo.InvariantCulture), right.Crc32.ToString("X8", CultureInfo.InvariantCulture), "The entry payload bytes differ.");
        AddIfDifferent(differences, name, "local-header offset", left.LocalHeaderOffset.ToString(CultureInfo.InvariantCulture), right.LocalHeaderOffset.ToString(CultureInfo.InvariantCulture), "Earlier entry sizes, ordering, or ZIP header fields differ.");
        AddIfDifferent(differences, name, "central-directory extra length", left.CentralDirectoryExtraLength.ToString(CultureInfo.InvariantCulture), right.CentralDirectoryExtraLength.ToString(CultureInfo.InvariantCulture), "Central-directory extra-field byte counts differ.");
        AddIfDifferent(differences, name, "central-directory extra fields", left.CentralDirectoryExtraFields, right.CentralDirectoryExtraFields, "Central-directory extra-field IDs or sizes differ; 0x0001 is ZIP64 extended information.");
        AddIfDifferent(differences, name, "local-header version-needed-to-extract", Version(left.LocalHeaderVersionNeeded), Version(right.LocalHeaderVersionNeeded), "Local-file-header version-needed metadata differs; 4.5 indicates ZIP64.");
        AddIfDifferent(differences, name, "local-header general-purpose flags", Flags(left.LocalHeaderGeneralPurposeFlags), Flags(right.LocalHeaderGeneralPurposeFlags), "Local-file-header general-purpose bit flags differ.");
        AddIfDifferent(differences, name, "local-header compression method", left.LocalHeaderCompressionMethod.ToString(CultureInfo.InvariantCulture), right.LocalHeaderCompressionMethod.ToString(CultureInfo.InvariantCulture), "Local-file-header compression method differs.");
        AddIfDifferent(differences, name, "local-header CRC-32", left.LocalHeaderCrc32.ToString("X8", CultureInfo.InvariantCulture), right.LocalHeaderCrc32.ToString("X8", CultureInfo.InvariantCulture), "Local-file-header CRC differs, often because bit 3 uses a data descriptor.");
        AddIfDifferent(differences, name, "local-header compressed size (32-bit field)", left.LocalHeaderCompressedSize32.ToString(CultureInfo.InvariantCulture), right.LocalHeaderCompressedSize32.ToString(CultureInfo.InvariantCulture), "Local-file-header 32-bit compressed-size field differs; 4294967295 indicates ZIP64 extra-field use.");
        AddIfDifferent(differences, name, "local-header uncompressed size (32-bit field)", left.LocalHeaderUncompressedSize32.ToString(CultureInfo.InvariantCulture), right.LocalHeaderUncompressedSize32.ToString(CultureInfo.InvariantCulture), "Local-file-header 32-bit uncompressed-size field differs; 4294967295 indicates ZIP64 extra-field use.");
        AddIfDifferent(differences, name, "local-header extra length", left.LocalHeaderExtraLength.ToString(CultureInfo.InvariantCulture), right.LocalHeaderExtraLength.ToString(CultureInfo.InvariantCulture), "Local-file-header extra-field byte counts differ and change AppxBlockMap LfhSize.");
        AddIfDifferent(differences, name, "local-header extra fields", left.LocalHeaderExtraFields, right.LocalHeaderExtraFields, "Local-file-header extra-field IDs or sizes differ; 0x0001 is ZIP64 extended information and changes AppxBlockMap LfhSize.");
    }

    private static (long EntryCount, long CentralDirectoryOffset) FindCentralDirectory(FileStream stream)
    {
        long length = stream.Length;
        int tailLength = checked((int)Math.Min(length, ushort.MaxValue + 22L));
        stream.Position = length - tailLength;
        byte[] tail = ReadBytes(stream, tailLength);
        int eocdInTail = FindEndOfCentralDirectory(tail);
        long eocd = length - tailLength + eocdInTail;
        long entryCount = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(eocdInTail + 10, 2));
        long centralDirectoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocdInTail + 16, 4));
        if (entryCount == ushort.MaxValue || centralDirectoryOffset == uint.MaxValue)
        {
            (entryCount, centralDirectoryOffset) = ReadZip64EndOfCentralDirectory(stream, eocd);
        }

        return (entryCount, centralDirectoryOffset);
    }

    private static int FindEndOfCentralDirectory(byte[] bytes)
    {
        for (int offset = bytes.Length - 22; offset >= 0; offset--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)) == EndOfCentralDirectorySignature)
            {
                return offset;
            }
        }

        throw new InvalidDataException("The ZIP end-of-central-directory record was not found.");
    }

    private static (long EntryCount, long CentralDirectoryOffset) ReadZip64EndOfCentralDirectory(FileStream stream, long eocd)
    {
        const uint Zip64LocatorSignature = 0x07064B50;
        const uint Zip64EndOfCentralDirectorySignature = 0x06064B50;
        if (eocd < 20)
        {
            throw new NotSupportedException("ZIP64 central-directory locator was not found.");
        }

        stream.Position = eocd - 20;
        Span<byte> locator = stackalloc byte[20];
        ReadExactly(stream, locator);
        if (BinaryPrimitives.ReadUInt32LittleEndian(locator[..4]) != Zip64LocatorSignature)
        {
            throw new NotSupportedException("ZIP64 central-directory locator was not found.");
        }

        ulong zip64Offset = BinaryPrimitives.ReadUInt64LittleEndian(locator.Slice(8, 8));
        stream.Position = checked((long)zip64Offset);
        Span<byte> zip64 = stackalloc byte[56];
        ReadExactly(stream, zip64);
        if (BinaryPrimitives.ReadUInt32LittleEndian(zip64[..4]) != Zip64EndOfCentralDirectorySignature)
        {
            throw new InvalidDataException("The ZIP64 end-of-central-directory record is malformed.");
        }

        ulong entryCount = BinaryPrimitives.ReadUInt64LittleEndian(zip64.Slice(32, 8));
        ulong centralDirectoryOffset = BinaryPrimitives.ReadUInt64LittleEndian(zip64.Slice(48, 8));
        return (checked((long)entryCount), checked((long)centralDirectoryOffset));
    }

    private static LocalHeaderInfo ReadLocalHeader(FileStream stream, long localHeaderOffset)
    {
        long returnPosition = stream.Position;
        stream.Position = localHeaderOffset;
        Span<byte> fixedHeader = stackalloc byte[30];
        ReadExactly(stream, fixedHeader);
        if (BinaryPrimitives.ReadUInt32LittleEndian(fixedHeader[..4]) != LocalFileHeaderSignature)
        {
            throw new InvalidDataException("The ZIP local file header is malformed.");
        }

        ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.Slice(26, 2));
        ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.Slice(28, 2));
        stream.Position += nameLength;
        byte[] extra = ReadBytes(stream, extraLength);
        stream.Position = returnPosition;
        return new LocalHeaderInfo(
            BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.Slice(4, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.Slice(6, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.Slice(8, 2)),
            BinaryPrimitives.ReadUInt32LittleEndian(fixedHeader.Slice(14, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(fixedHeader.Slice(18, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(fixedHeader.Slice(22, 4)),
            extraLength,
            extra);
    }

    private static void ApplyZip64Extra(
        ReadOnlySpan<byte> extra,
        uint compressedSize32,
        uint uncompressedSize32,
        uint localHeaderOffset32,
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
                if (uncompressedSize32 == uint.MaxValue)
                {
                    uncompressedSize = ReadZip64Value(zip64, ref zip64Position);
                }

                if (compressedSize32 == uint.MaxValue)
                {
                    compressedSize = ReadZip64Value(zip64, ref zip64Position);
                }

                if (localHeaderOffset32 == uint.MaxValue)
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

    private static string FormatExtraFields(ReadOnlySpan<byte> extra)
    {
        if (extra.IsEmpty)
        {
            return "<none>";
        }

        var fields = new List<string>();
        int position = 0;
        while (position + 4 <= extra.Length)
        {
            ushort headerId = BinaryPrimitives.ReadUInt16LittleEndian(extra.Slice(position, 2));
            ushort dataSize = BinaryPrimitives.ReadUInt16LittleEndian(extra.Slice(position + 2, 2));
            position += 4;
            if (position + dataSize > extra.Length)
            {
                fields.Add("malformed");
                break;
            }

            fields.Add("0x" + headerId.ToString("X4", CultureInfo.InvariantCulture) + "(" + dataSize.ToString(CultureInfo.InvariantCulture) + ")");
            position += dataSize;
        }

        if (position != extra.Length)
        {
            fields.Add("trailing(" + (extra.Length - position).ToString(CultureInfo.InvariantCulture) + ")");
        }

        return string.Join(", ", fields);
    }

    private static string Version(ushort value) => (value / 10).ToString(CultureInfo.InvariantCulture) + "." + (value % 10).ToString(CultureInfo.InvariantCulture) + " (" + value.ToString(CultureInfo.InvariantCulture) + ")";

    private static string Flags(ushort value)
    {
        var names = new List<string>();
        if ((value & 0x0008) != 0)
        {
            names.Add("data-descriptor");
        }

        if ((value & 0x0800) != 0)
        {
            names.Add("utf8");
        }

        return "0x" + value.ToString("X4", CultureInfo.InvariantCulture) + (names.Count == 0 ? string.Empty : " [" + string.Join(", ", names) + "]");
    }

    private static byte[] ReadBytes(Stream stream, int count)
    {
        byte[] bytes = new byte[count];
        ReadExactly(stream, bytes);
        return bytes;
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int current = stream.Read(buffer[read..]);
            if (current == 0)
            {
                throw new EndOfStreamException();
            }

            read += current;
        }
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

    private sealed record LocalHeaderInfo(
        ushort VersionNeeded,
        ushort GeneralPurposeFlags,
        ushort CompressionMethod,
        uint Crc32,
        uint CompressedSize32,
        uint UncompressedSize32,
        ushort ExtraLength,
        byte[] Extra);
}
