using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace MsixCore.Packaging.Opc;

/// <summary>
/// Cross-platform <see cref="IOpcPackage"/> implementation backed by
/// <see cref="System.IO.Compression.ZipArchive"/>. Every MSIX/APPX package (and bundle) is an OPC
/// ZIP container, so this is the foundation the rest of the reader builds on.
/// </summary>
/// <remarks>
/// Instances are read-only but not thread-safe: concurrent calls to <see cref="OpenPart(string)"/>
/// share the underlying <see cref="ZipArchive"/> and must be externally synchronized.
/// </remarks>
public sealed class OpcPackage : IOpcPackage
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entriesByPart;
    private readonly Dictionary<string, OpcPartZipInfo> _zipInfoByPart;
    private readonly List<string> _partNames;
    private readonly Stream? _callerStream;
    private readonly CentralDirectorySnapshot? _centralDirectorySnapshot;
    private readonly string? _snapshotUnavailableReason;

    private OpcPackage(ZipArchive archive, Stream? callerStream, IReadOnlyList<CentralDirectoryRecord>? centralDirectory)
    {
        _archive = archive;
        _callerStream = callerStream;
        _entriesByPart = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        _zipInfoByPart = new Dictionary<string, OpcPartZipInfo>(StringComparer.OrdinalIgnoreCase);
        _partNames = new List<string>(archive.Entries.Count);

        if (centralDirectory is not null)
        {
            VerifyCentralDirectoryBinding(archive, centralDirectory);
        }

        for (int entryIndex = 0; entryIndex < archive.Entries.Count; entryIndex++)
        {
            ZipArchiveEntry entry = archive.Entries[entryIndex];
            // Skip directory entries (zip directory markers end with '/').
            if (entry.FullName.EndsWith('/'))
            {
                continue;
            }

            if (!IsValidPartName(entry.FullName))
            {
                throw MsixError.Format(MsixErrorCode.PartName,
                    $"The package contains an invalid OPC part name: '{entry.FullName}'.");
            }

            // OPC part names are percent-encoded in the ZIP (e.g. '!' -> '%21'), but the block map,
            // manifest, and file system all use the decoded logical name. Canonicalize to the decoded
            // form so lookups and coverage checks line up. Decode each '/'-delimited segment on its own
            // and reject an encoded separator ('%2f'/'%5c') so it can't smuggle in a new path boundary,
            // then re-validate because decoding can reintroduce traversal segments (e.g. '%2e%2e' -> '..').
            if (!TryCanonicalizePartName(entry.FullName, out string partName) || !IsValidPartName(partName))
            {
                throw MsixError.Format(MsixErrorCode.PartName,
                    $"The package contains an invalid OPC part name: '{entry.FullName}'.");
            }

            // OPC forbids equivalent (including case-insensitively equal) part names.
            if (!_entriesByPart.TryAdd(partName, entry))
            {
                throw MsixError.Format(MsixErrorCode.PartName,
                    $"The package contains a duplicate OPC part name: '{partName}'.");
            }

            if (centralDirectory is not null)
            {
                _zipInfoByPart.Add(
                    partName,
                    new OpcPartZipInfo(
                        entry.Length,
                        entry.CompressedLength,
                        centralDirectory[entryIndex].CompressionMethod != 0));
            }
            _partNames.Add(partName);
        }

        if (callerStream is not null)
        {
            if (TryCaptureCentralDirectory(callerStream, out CentralDirectorySnapshot? snapshot, out string? error))
            {
                _centralDirectorySnapshot = snapshot;
            }
            else
            {
                _snapshotUnavailableReason = error;
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> PartNames => _partNames;

    /// <inheritdoc/>
    public string? DetectSnapshotDrift()
    {
        if (_callerStream is null)
        {
            // File-path opens own the read-only handle used by the one ZipArchive instance, so its
            // immutable central-directory snapshot has no separately supplied backing stream.
            return null;
        }

        if (_centralDirectorySnapshot is null)
        {
            return $"Caller-supplied stream consistency cannot be established: {_snapshotUnavailableReason}";
        }

        if (!TryCaptureCentralDirectory(
                _callerStream,
                out CentralDirectorySnapshot? current,
                out string? error))
        {
            return $"Caller-supplied stream consistency cannot be established: {error}";
        }

        CentralDirectorySnapshot original = _centralDirectorySnapshot;
        if (current!.ContainerLength != original.ContainerLength
            || current.CentralDirectoryOffset != original.CentralDirectoryOffset
            || current.CentralDirectorySize != original.CentralDirectorySize
            || !CryptographicOperations.FixedTimeEquals(current.Digest, original.Digest))
        {
            return "The caller-supplied stream's ZIP central directory or container length changed since the package was opened.";
        }

        return null;
    }

    /// <summary>Opens an OPC package from a file path.</summary>
    /// <param name="path">Path to the <c>.msix</c>/<c>.appx</c>/bundle file.</param>
    /// <returns>An open <see cref="OpcPackage"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or empty.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is not a valid ZIP/OPC container.</exception>
    public static OpcPackage Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The package file was not found.", path);
        }

        FileStream fileStream = File.OpenRead(path);
        try
        {
            return OpenCore(fileStream, leaveOpen: false, callerSupplied: false);
        }
        catch
        {
            fileStream.Dispose();
            throw;
        }
    }

    /// <summary>Opens an OPC package from a readable stream.</summary>
    /// <param name="stream">A readable stream positioned at the start of the package.</param>
    /// <param name="leaveOpen">Whether to leave <paramref name="stream"/> open when this package is disposed.</param>
    /// <returns>An open <see cref="OpcPackage"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The stream is not a valid ZIP/OPC container.</exception>
    public static OpcPackage Open(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return OpenCore(stream, leaveOpen, callerSupplied: true);
    }

    private static OpcPackage OpenCore(Stream stream, bool leaveOpen, bool callerSupplied)
    {
        var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen);
        try
        {
            // The central directory is read lazily, so validation failures can surface here.
            List<CentralDirectoryRecord>? centralDirectory = stream.CanSeek
                ? ReadCentralDirectoryRecords(stream, archive.Entries.Count)
                : null;
            return new OpcPackage(archive, callerSupplied ? stream : null, centralDirectory);
        }
        catch
        {
            // Disposing the archive respects leaveOpen: the caller's stream is preserved when requested.
            archive.Dispose();
            throw;
        }
    }

    /// <summary>
    /// One central-directory record as read by this type's own parser, retained so the result can be
    /// bound back to the corresponding <see cref="ZipArchiveEntry"/> by identity rather than position.
    /// </summary>
    private readonly record struct CentralDirectoryRecord(
        ushort CompressionMethod,
        uint Crc32,
        uint CompressedSize,
        uint UncompressedSize);

    /// <summary>
    /// Confirms that this type's central-directory parse describes exactly the same entries, in the
    /// same order, as <see cref="ZipArchive"/>'s parse.
    /// </summary>
    /// <remarks>
    /// The compression method is only available from the raw central directory, so it is read by a
    /// second, independent parser and then matched positionally against <see cref="ZipArchive.Entries"/>.
    /// Two parsers reading one attacker-controlled file is a trust boundary: if they could ever be made
    /// to disagree about which records exist or what order they are in, one part's compression method
    /// would be attributed to another part, letting an attacker choose which branch of the block-map
    /// size check runs. Position alone is not evidence that they agree, so each record is bound to its
    /// entry by CRC-32 and sizes. Any divergence - from a differing end-of-central-directory or ZIP64
    /// interpretation, from reordering, or from a future runtime change - fails closed here instead of
    /// silently mislabelling a part. ZIP64 saturates the 32-bit size fields, so those are compared only
    /// when not saturated; the CRC is never saturated and is always compared.
    /// </remarks>
    private static void VerifyCentralDirectoryBinding(
        ZipArchive archive,
        IReadOnlyList<CentralDirectoryRecord> centralDirectory)
    {
        if (centralDirectory.Count != archive.Entries.Count)
        {
            throw MsixError.Format(MsixErrorCode.ZipStructure, "The ZIP central-directory entry count is inconsistent.");
        }

        for (int i = 0; i < centralDirectory.Count; i++)
        {
            ZipArchiveEntry entry = archive.Entries[i];
            CentralDirectoryRecord record = centralDirectory[i];

            if (record.Crc32 != entry.Crc32
                || (record.CompressedSize != uint.MaxValue && record.CompressedSize != entry.CompressedLength)
                || (record.UncompressedSize != uint.MaxValue && record.UncompressedSize != entry.Length))
            {
                throw MsixError.Format(MsixErrorCode.ZipStructure,
                    $"The ZIP central directory is inconsistent for entry '{entry.FullName}'.");
            }
        }
    }

    private static List<CentralDirectoryRecord> ReadCentralDirectoryRecords(Stream stream, int entryCount)
    {
        const uint centralHeaderSignature = 0x02014B50;
        long originalPosition = stream.Position;
        try
        {
            if (!TryReadCentralDirectoryLocation(
                    stream,
                    stream.Length,
                    out long centralOffset,
                    out _,
                    out string? error))
            {
                throw MsixError.Format(MsixErrorCode.ZipStructure, $"The ZIP central directory is invalid: {error}");
            }

            var records = new List<CentralDirectoryRecord>(entryCount);
            stream.Position = centralOffset;
            Span<byte> header = stackalloc byte[46];
            for (int i = 0; i < entryCount; i++)
            {
                if (!ReadExactly(stream, header)
                    || BinaryPrimitives.ReadUInt32LittleEndian(header[..4]) != centralHeaderSignature)
                {
                    throw MsixError.Format(MsixErrorCode.ZipStructure, "The ZIP central directory contains an invalid entry header.");
                }

                records.Add(new CentralDirectoryRecord(
                    BinaryPrimitives.ReadUInt16LittleEndian(header[10..12]),
                    BinaryPrimitives.ReadUInt32LittleEndian(header[16..20]),
                    BinaryPrimitives.ReadUInt32LittleEndian(header[20..24]),
                    BinaryPrimitives.ReadUInt32LittleEndian(header[24..28])));
                int variableLength =
                    BinaryPrimitives.ReadUInt16LittleEndian(header[28..30])
                    + BinaryPrimitives.ReadUInt16LittleEndian(header[30..32])
                    + BinaryPrimitives.ReadUInt16LittleEndian(header[32..34]);
                stream.Position = checked(stream.Position + variableLength);
            }

            return records;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static bool TryCaptureCentralDirectory(
        Stream stream,
        out CentralDirectorySnapshot? snapshot,
        out string? error)
    {
        snapshot = null;
        error = null;
        if (!stream.CanRead || !stream.CanSeek)
        {
            error = "the stream is not both readable and seekable.";
            return false;
        }

        long originalPosition;
        try
        {
            originalPosition = stream.Position;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ObjectDisposedException)
        {
            error = $"the stream position is unavailable: {ex.Message}";
            return false;
        }

        bool positionRestored = false;
        try
        {
            long length = stream.Length;
            if (!TryReadCentralDirectoryLocation(
                    stream,
                    length,
                    out long centralOffset,
                    out long centralSize,
                    out error))
            {
                return false;
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                stream.Position = centralOffset;
                long remaining = centralSize;
                while (remaining > 0)
                {
                    int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read == 0)
                    {
                        error = "the ZIP central directory ended unexpectedly.";
                        return false;
                    }

                    hash.AppendData(buffer, 0, read);
                    remaining -= read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            var captured = new CentralDirectorySnapshot(
                length,
                centralOffset,
                centralSize,
                hash.GetHashAndReset());
            stream.Position = originalPosition;
            positionRestored = true;
            snapshot = captured;
            return true;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ObjectDisposedException)
        {
            error = $"the stream could not be re-read: {ex.Message}";
            return false;
        }
        finally
        {
            try
            {
                if (!positionRestored)
                {
                    stream.Position = originalPosition;
                }
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ObjectDisposedException)
            {
                error ??= $"the stream position could not be restored: {ex.Message}";
                snapshot = null;
            }
        }
    }

    private static bool TryReadCentralDirectoryLocation(
        Stream stream,
        long length,
        out long centralOffset,
        out long centralSize,
        out string? error)
    {
        const uint eocdSignature = 0x06054B50;
        const uint zip64LocatorSignature = 0x07064B50;
        const uint zip64EocdSignature = 0x06064B50;
        const int minimumEocdSize = 22;
        const int maximumCommentLength = ushort.MaxValue;

        centralOffset = 0;
        centralSize = 0;
        error = null;
        if (length < minimumEocdSize)
        {
            error = "the stream is too short to contain a ZIP end-of-central-directory record.";
            return false;
        }

        int tailLength = (int)Math.Min(length, minimumEocdSize + maximumCommentLength);
        byte[] tail = new byte[tailLength];
        stream.Position = length - tailLength;
        if (!ReadExactly(stream, tail))
        {
            error = "the ZIP end-of-central-directory record could not be read.";
            return false;
        }

        int eocdIndex = -1;
        for (int i = tail.Length - minimumEocdSize; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i, 4)) != eocdSignature)
            {
                continue;
            }

            int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20, 2));
            if (i + minimumEocdSize + commentLength == tail.Length)
            {
                eocdIndex = i;
                break;
            }
        }

        if (eocdIndex < 0)
        {
            error = "a terminal ZIP end-of-central-directory record was not found.";
            return false;
        }

        ReadOnlySpan<byte> eocd = tail.AsSpan(eocdIndex, minimumEocdSize);
        uint size32 = BinaryPrimitives.ReadUInt32LittleEndian(eocd[12..16]);
        uint offset32 = BinaryPrimitives.ReadUInt32LittleEndian(eocd[16..20]);
        if (size32 != uint.MaxValue && offset32 != uint.MaxValue)
        {
            centralSize = size32;
            centralOffset = offset32;
        }
        else
        {
            long eocdOffset = length - tailLength + eocdIndex;
            if (eocdOffset < 20)
            {
                error = "the ZIP64 end-of-central-directory locator is missing.";
                return false;
            }

            Span<byte> locator = stackalloc byte[20];
            stream.Position = eocdOffset - locator.Length;
            if (!ReadExactly(stream, locator)
                || BinaryPrimitives.ReadUInt32LittleEndian(locator[..4]) != zip64LocatorSignature)
            {
                error = "the ZIP64 end-of-central-directory locator is invalid.";
                return false;
            }

            ulong zip64Offset = BinaryPrimitives.ReadUInt64LittleEndian(locator[8..16]);
            if (zip64Offset > long.MaxValue)
            {
                error = "the ZIP64 end-of-central-directory offset is out of range.";
                return false;
            }

            Span<byte> zip64 = stackalloc byte[56];
            stream.Position = (long)zip64Offset;
            if (!ReadExactly(stream, zip64)
                || BinaryPrimitives.ReadUInt32LittleEndian(zip64[..4]) != zip64EocdSignature)
            {
                error = "the ZIP64 end-of-central-directory record is invalid.";
                return false;
            }

            ulong size64 = BinaryPrimitives.ReadUInt64LittleEndian(zip64[40..48]);
            ulong offset64 = BinaryPrimitives.ReadUInt64LittleEndian(zip64[48..56]);
            if (size64 > long.MaxValue || offset64 > long.MaxValue)
            {
                error = "the ZIP64 central-directory range is out of range.";
                return false;
            }

            centralSize = (long)size64;
            centralOffset = (long)offset64;
        }

        if (centralOffset < 0 || centralSize < 0 || centralOffset > length - centralSize)
        {
            error = "the ZIP central-directory range lies outside the stream.";
            return false;
        }

        return true;
    }

    private static bool ReadExactly(Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer[total..]);
            if (read == 0)
            {
                return false;
            }

            total += read;
        }

        return true;
    }

    private sealed record CentralDirectorySnapshot(
        long ContainerLength,
        long CentralDirectoryOffset,
        long CentralDirectorySize,
        byte[] Digest);

    /// <inheritdoc/>
    public bool ContainsPart(string partName)
    {
        ArgumentException.ThrowIfNullOrEmpty(partName);
        return _entriesByPart.ContainsKey(NormalizeLookup(partName));
    }

    /// <inheritdoc/>
    public OpcPartZipInfo? GetZipInfo(string partName)
    {
        ArgumentException.ThrowIfNullOrEmpty(partName);
        return _zipInfoByPart.TryGetValue(NormalizeLookup(partName), out OpcPartZipInfo? info)
            ? info
            : null;
    }

    /// <inheritdoc/>
    public Stream OpenPart(string partName)
    {
        ArgumentException.ThrowIfNullOrEmpty(partName);

        if (!_entriesByPart.TryGetValue(NormalizeLookup(partName), out ZipArchiveEntry? entry))
        {
            throw new FileNotFoundException($"Part '{partName}' was not found in the package.", partName);
        }

        return entry.Open();
    }

    /// <inheritdoc/>
    public void Dispose() => _archive.Dispose();

    /// <summary>
    /// Percent-decodes a raw ZIP entry name into its canonical OPC logical part name. OPC stores part
    /// names percent-encoded (UTF-8), so <c>%21</c> becomes <c>!</c> and <c>%20</c> becomes a space,
    /// matching the unencoded names used by the block map and manifest. Each <c>/</c>-delimited segment
    /// is decoded independently; a segment that decodes to contain a <c>/</c> or <c>\</c> (an encoded
    /// separator, which OPC forbids) causes this to return <see langword="false"/>.
    /// </summary>
    internal static bool TryCanonicalizePartName(string rawName, out string canonical)
    {
        string[] segments = rawName.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            if (!TryPercentDecodeSegment(segments[i], out string decoded))
            {
                canonical = string.Empty;
                return false;
            }

            if (decoded.Contains('/') || decoded.Contains('\\'))
            {
                canonical = string.Empty;
                return false;
            }

            // Reject control characters (e.g. NUL from '%00'), which are invalid in OPC part names and
            // would otherwise pass validation only to surface later as an ArgumentException from the
            // filesystem path APIs during extraction.
            foreach (char c in decoded)
            {
                if (char.IsControl(c))
                {
                    canonical = string.Empty;
                    return false;
                }
            }

            segments[i] = decoded;
        }

        canonical = string.Join('/', segments);
        return true;
    }

    private static bool TryPercentDecodeSegment(string segment, out string decoded)
    {
        var bytes = new List<byte>(segment.Length);
        for (int i = 0; i < segment.Length;)
        {
            if (segment[i] == '%')
            {
                if (i + 2 >= segment.Length
                    || !TryReadHexByte(segment[i + 1], segment[i + 2], out byte value))
                {
                    decoded = string.Empty;
                    return false;
                }

                bytes.Add(value);
                i += 3;
                continue;
            }

            int charCount = 1;
            if (char.IsHighSurrogate(segment[i]))
            {
                if (i + 1 >= segment.Length || !char.IsLowSurrogate(segment[i + 1]))
                {
                    decoded = string.Empty;
                    return false;
                }

                charCount = 2;
            }
            else if (char.IsLowSurrogate(segment[i]))
            {
                decoded = string.Empty;
                return false;
            }

            byte[] literal = StrictUtf8.GetBytes(segment.Substring(i, charCount));
            bytes.AddRange(literal);
            i += charCount;
        }

        try
        {
            decoded = StrictUtf8.GetString(bytes.ToArray());
            return true;
        }
        catch (DecoderFallbackException)
        {
            decoded = string.Empty;
            return false;
        }
    }

    private static bool TryReadHexByte(char high, char low, out byte value)
    {
        if (!TryReadHexNibble(high, out int highValue) || !TryReadHexNibble(low, out int lowValue))
        {
            value = 0;
            return false;
        }

        value = (byte)((highValue << 4) | lowValue);
        return true;
    }

    private static bool TryReadHexNibble(char c, out int value)
    {
        if (c is >= '0' and <= '9')
        {
            value = c - '0';
            return true;
        }

        if (c is >= 'A' and <= 'F')
        {
            value = c - 'A' + 10;
            return true;
        }

        if (c is >= 'a' and <= 'f')
        {
            value = c - 'a' + 10;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// Leniently normalizes a caller-supplied part name for lookup: backslashes become forward
    /// slashes and a single leading slash is trimmed. Stored part names are already canonical.
    /// </summary>
    internal static string NormalizeLookup(string name)
    {
        string normalized = name.Replace('\\', '/');
        return normalized.StartsWith('/') ? normalized[1..] : normalized;
    }

    /// <summary>
    /// Validates a raw ZIP entry name against OPC and Windows package-path rules: it must be
    /// package-relative, use forward slashes, and contain only segments that Windows can safely
    /// materialize without traversal or drive/alternate-stream interpretation.
    /// </summary>
    internal static bool IsValidPartName(string rawName)
    {
        if (string.IsNullOrEmpty(rawName) || rawName.StartsWith('/') || rawName.Contains('\\'))
        {
            return false;
        }

        foreach (string segment in rawName.Split('/'))
        {
            if (segment.Length == 0
                || segment == "."
                || segment == ".."
                || segment.EndsWith(' ')
                || segment.EndsWith('.'))
            {
                return false;
            }

            for (int i = 0; i < segment.Length; i++)
            {
                char character = segment[i];
                if (char.IsControl(character) || "<>:\"|?*".Contains(character, StringComparison.Ordinal))
                {
                    return false;
                }

                // Reject unpaired surrogates: part names are encoded as strict UTF-8, which would
                // otherwise throw only once the package is being written.
                if (char.IsHighSurrogate(character))
                {
                    if (i + 1 >= segment.Length || !char.IsLowSurrogate(segment[i + 1]))
                    {
                        return false;
                    }

                    i++;
                }
                else if (char.IsLowSurrogate(character))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
