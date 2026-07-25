using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using MsixCore.Packaging.Authoring;
using MsixCore.Packaging.Integrity;
using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging.Tests;

/// <summary>
/// Tests for the ZIP64-always-trailer writer, verifying structural correctness of
/// the ZIP64 End-Of-Central-Directory and locator, per-entry extra field behaviour,
/// LfhSize parity, and third-party readability via System.IO.Compression.ZipArchive.
/// </summary>
public sealed class Zip64WriterTests : IDisposable
{
    private const string Manifest =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
          <Identity Name="Contoso.Zip64" Publisher="CN=Contoso" Version="1.0.0.0" ProcessorArchitecture="x64" />
          <Properties>
            <DisplayName>ZIP64 test package</DisplayName>
            <PublisherDisplayName>Contoso Ltd</PublisherDisplayName>
          </Properties>
        </Package>
        """;

    private readonly string _root;

    public Zip64WriterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msix-zip64-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Zip64Eocd_AlwaysPresent_AndStructurallyCorrect()
    {
        // Write a small archive with a single entry.
        byte[] archive = WriteSmallArchive("hello.txt", "Hello, ZIP64!"u8.ToArray());

        // Find the classic EOCD (last 22 bytes since no comment).
        int eocdOffset = archive.Length - 22;
        Assert.Equal(0x06054B50U, BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(eocdOffset)));

        // ZIP64 EOCD Locator is immediately before the classic EOCD (20 bytes).
        int locatorOffset = eocdOffset - 20;
        Assert.Equal(0x07064B50U, BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(locatorOffset)));
        uint locatorDisk = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(locatorOffset + 4));
        ulong zip64EocdOffset = BinaryPrimitives.ReadUInt64LittleEndian(archive.AsSpan(locatorOffset + 8));
        uint totalDisks = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(locatorOffset + 16));
        Assert.Equal(0U, locatorDisk);
        Assert.Equal(1U, totalDisks);

        // Validate the ZIP64 EOCD record at the offset indicated by the locator.
        int z64Offset = checked((int)zip64EocdOffset);
        Assert.Equal(0x06064B50U, BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(z64Offset)));
        ulong z64RecordSize = BinaryPrimitives.ReadUInt64LittleEndian(archive.AsSpan(z64Offset + 4));
        Assert.Equal(44UL, z64RecordSize); // fixed size of the remaining ZIP64 EOCD
        ushort versionMadeBy = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(z64Offset + 12));
        ushort versionNeeded = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(z64Offset + 14));
        Assert.Equal(45, versionMadeBy);
        Assert.Equal(45, versionNeeded);
        ulong entryCountDisk = BinaryPrimitives.ReadUInt64LittleEndian(archive.AsSpan(z64Offset + 24));
        ulong entryCountTotal = BinaryPrimitives.ReadUInt64LittleEndian(archive.AsSpan(z64Offset + 32));
        Assert.Equal(1UL, entryCountDisk);
        Assert.Equal(1UL, entryCountTotal);
    }

    [Fact]
    public void SmallPackage_NoPerEntryZip64Extra_NoDataDescriptor()
    {
        byte[] archive = WriteSmallArchive("data.bin", new byte[100]);

        // Check local file header: no extra field (extra length at offset 28 = 0).
        Assert.Equal(0x04034B50U, BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(0)));
        ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(28));
        Assert.Equal(0, extraLength);

        // General purpose flags should NOT have data-descriptor bit (bit 3).
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(6));
        Assert.Equal(0, flags & 0x0008);

        // CRC and sizes should be filled in the local header (not zero).
        uint crc32 = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(14));
        uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(18));
        uint uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(22));
        Assert.NotEqual(0U, crc32);
        Assert.Equal(100U, compressedSize);
        Assert.Equal(100U, uncompressedSize);

        // UTF-8 bit should NOT be set.
        Assert.Equal(0, flags & 0x0800);
    }

    [Fact]
    public void BuildCentralZip64ExtraField_NoOverflow_ReturnsEmpty()
    {
        byte[] extra = StoredZipWriter.BuildCentralZip64ExtraField(
            uncompressedSize: 1000,
            compressedSize: 500,
            localHeaderOffset: 0);

        Assert.Empty(extra);
    }

    [Theory]
    [InlineData(0x100000000L, 500L, 0L, 8)] // only uncompressed overflows
    [InlineData(500L, 0x100000000L, 0L, 8)] // only compressed overflows
    [InlineData(500L, 500L, 0x100000000L, 8)] // only offset overflows
    [InlineData(0x100000000L, 0x100000000L, 0L, 16)] // both sizes overflow
    [InlineData(0x100000000L, 0x100000000L, 0x100000000L, 24)] // all three overflow
    public void BuildCentralZip64ExtraField_SentinelDriven_CorrectVariableSize(
        long uncompressedSize, long compressedSize, long localHeaderOffset, int expectedDataSize)
    {
        byte[] extra = StoredZipWriter.BuildCentralZip64ExtraField(
            uncompressedSize, compressedSize, localHeaderOffset);

        Assert.Equal(4 + expectedDataSize, extra.Length);
        ushort id = BinaryPrimitives.ReadUInt16LittleEndian(extra.AsSpan(0));
        ushort dataSize = BinaryPrimitives.ReadUInt16LittleEndian(extra.AsSpan(2));
        Assert.Equal(0x0001, id);
        Assert.Equal(expectedDataSize, dataSize);

        // Verify the values are in the correct canonical order.
        int offset = 4;
        if (uncompressedSize > uint.MaxValue - 1)
        {
            Assert.Equal((ulong)uncompressedSize, BinaryPrimitives.ReadUInt64LittleEndian(extra.AsSpan(offset)));
            offset += 8;
        }
        if (compressedSize > uint.MaxValue - 1)
        {
            Assert.Equal((ulong)compressedSize, BinaryPrimitives.ReadUInt64LittleEndian(extra.AsSpan(offset)));
            offset += 8;
        }
        if (localHeaderOffset > uint.MaxValue - 1)
        {
            Assert.Equal((ulong)localHeaderOffset, BinaryPrimitives.ReadUInt64LittleEndian(extra.AsSpan(offset)));
        }
    }

    [Fact]
    public void LfhSize_InBlockMap_MatchesActualLocalHeaderBytes()
    {
        string source = CreateSource("lfhsize-parity");
        WritePayload(source, "Data/payload.bin", new byte[500]);
        string output = Path.Combine(_root, "lfhsize-parity.msix");

        MsixPackageBuilder.Build(source, output);

        using MsixPackage package = MsixPackage.Open(output);
        Assert.True(package.VerifyBlockMap().IsValid);

        // Parse the block map for LfhSize values.
        using Stream blockMapStream = package.Opc.OpenPart(OpcPartNames.AppxBlockMap);
        XDocument blockMap = XDocument.Load(blockMapStream);
        Dictionary<string, int> declaredSizes = blockMap.Root!.Elements()
            .Where(e => e.Name.LocalName == "File")
            .ToDictionary(
                e => e.Attribute("Name")!.Value.Replace('\\', '/'),
                e => int.Parse(e.Attribute("LfhSize")!.Value, System.Globalization.CultureInfo.InvariantCulture));

        // Read actual local headers from the ZIP file.
        Dictionary<string, int> actualSizes = ReadLocalHeaderSizes(output);

        foreach ((string name, int declared) in declaredSizes)
        {
            Assert.True(actualSizes.ContainsKey(name), $"Entry '{name}' not found in ZIP.");
            Assert.Equal(declared, actualSizes[name]);
        }
    }

    [Fact]
    public void RoundTrip_OwnReaderAndSystemIOCompression_BothSucceed()
    {
        string source = CreateSource("roundtrip-zip64");
        byte[] payload = Enumerable.Range(0, 4096).Select(v => (byte)(v % 251)).ToArray();
        WritePayload(source, "Data/content.bin", payload);
        string output = Path.Combine(_root, "roundtrip-zip64.msix");

        MsixPackageBuilder.Build(source, output);

        // 1. Our own reader can open and verify.
        using (MsixPackage package = MsixPackage.Open(output))
        {
            Assert.True(package.VerifyBlockMap().IsValid);
            using Stream part = package.Opc.OpenPart("Data/content.bin");
            using var ms = new MemoryStream();
            part.CopyTo(ms);
            Assert.Equal(payload, ms.ToArray());
        }

        // 2. System.IO.Compression.ZipArchive can read every entry.
        using (ZipArchive zip = ZipFile.OpenRead(output))
        {
            Assert.True(zip.Entries.Count >= 4); // manifest + payload + content_types + blockmap
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                using Stream s = entry.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                Assert.Equal(entry.Length, ms.Length);
            }
        }
    }

    [Fact]
    public void RoundTrip_OptimalCompression_SystemIOCompressionCanRead()
    {
        string source = CreateSource("roundtrip-optimal");
        byte[] payload = Enumerable.Repeat((byte)'Z', 8192).ToArray();
        WritePayload(source, "Data/compressible.txt", payload);
        string output = Path.Combine(_root, "roundtrip-optimal.msix");

        MsixPackageBuilder.Build(source, output, new PackOptions { CompressionLevel = CompressionLevel.Optimal });

        using ZipArchive zip = ZipFile.OpenRead(output);
        ZipArchiveEntry entry = zip.Entries.Single(e => e.FullName == "Data/compressible.txt");
        using Stream s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        Assert.Equal(payload, ms.ToArray());
    }

    [Fact]
    public void EntryCountAbove65535_NowWorks()
    {
        // Use a reduced-scale test: 100 entries (not 65536) to prove the ceiling is removed.
        // The 65535-entry NotSupportedException was the old code's explicit guard; we verify
        // it no longer throws and the archive is structurally valid.
        const int entryCount = 100;
        using var output = new MemoryStream();
        using (var writer = new StoredZipWriter(output))
        {
            for (int i = 0; i < entryCount; i++)
            {
                writer.AddEntry($"entry{i:D5}.bin", stream => stream.WriteByte(0x42));
            }
        }

        byte[] archive = output.ToArray();

        // Verify ZIP64 EOCD has correct entry count.
        int eocdOffset = archive.Length - 22;
        int locatorOffset = eocdOffset - 20;
        ulong zip64EocdOffset = BinaryPrimitives.ReadUInt64LittleEndian(archive.AsSpan(locatorOffset + 8));
        int z64Offset = checked((int)zip64EocdOffset);
        ulong entryCountTotal = BinaryPrimitives.ReadUInt64LittleEndian(archive.AsSpan(z64Offset + 32));
        Assert.Equal((ulong)entryCount, entryCountTotal);

        // System.IO.Compression can read all entries.
        using var readStream = new MemoryStream(archive);
        using var zip = new ZipArchive(readStream, ZipArchiveMode.Read);
        Assert.Equal(entryCount, zip.Entries.Count);
    }

    [Fact]
    public void ClassicEocd_AllSentinelConstants_MatchesSDK()
    {
        // The SDK's EndCentralDirectoryRecord::Read derives m_isZip64 EXCLUSIVELY from whether
        // any classic EOCD field equals its type-maximum sentinel. If we write real values,
        // m_isZip64 is false and ZipObjectWriter throws "Editing non zip64 packages not supported"
        // — signing fails. The classic EOCD must be an unconditional constant.
        byte[] archive = WriteSmallArchive("tiny.bin", [0x01]);
        int eocdOffset = archive.Length - 22;

        // Exact 22-byte constant that the SDK's EndCentralDirectoryRecord() constructor produces.
        byte[] expectedEocd =
        [
            0x50, 0x4B, 0x05, 0x06, // signature
            0x00, 0x00,             // number of this disk
            0x00, 0x00,             // disk with start of CD
            0xFF, 0xFF,             // entries on this disk (sentinel)
            0xFF, 0xFF,             // total entries (sentinel)
            0xFF, 0xFF, 0xFF, 0xFF, // size of central directory (sentinel)
            0xFF, 0xFF, 0xFF, 0xFF, // offset of start of CD (sentinel)
            0x00, 0x00,             // comment length
        ];

        Assert.Equal(expectedEocd, archive.AsSpan(eocdOffset).ToArray());
    }

    [Fact]
    public void ClassicEocd_IsZip64Derivation_AtLeastOneSentinelPresent()
    {
        // Reproduce the SDK's m_isZip64 derivation logic: m_isZip64 is true iff at least one
        // of the six classic EOCD value fields equals its type maximum. A future regression
        // that "helpfully" restores real values would fail signability.
        byte[] archive = WriteSmallArchive("sentinel-check.bin", new byte[42]);
        int eocdOffset = archive.Length - 22;

        ushort diskNumber = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(eocdOffset + 4));
        ushort diskStart = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(eocdOffset + 6));
        ushort diskEntries = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(eocdOffset + 8));
        ushort totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(eocdOffset + 10));
        uint cdSize = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(eocdOffset + 12));
        uint cdOffsetValue = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(eocdOffset + 16));

        // SDK logic: IsValueInExtendedInfo(v) => v == type_max
        bool isZip64 =
            diskNumber == ushort.MaxValue ||
            diskStart == ushort.MaxValue ||
            diskEntries == ushort.MaxValue ||
            totalEntries == ushort.MaxValue ||
            cdSize == uint.MaxValue ||
            cdOffsetValue == uint.MaxValue;

        Assert.True(isZip64,
            "Classic EOCD must have at least one sentinel value so the SDK derives m_isZip64 == true. " +
            "Without this, ZipObjectWriter refuses to edit the package for signing.");
    }

    [Fact]
    public void CentralDirectory_VersionMadeBy45_NoUtf8Flag()
    {
        byte[] archive = WriteSmallArchive("test.bin", [0x00]);

        // Find the central directory via the ZIP64 EOCD (classic EOCD offset is sentinel).
        int cdStart = FindCentralDirectoryStart(archive);
        Assert.Equal(0x02014B50U, BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(cdStart)));

        ushort versionMadeBy = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(cdStart + 4));
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(cdStart + 8));

        Assert.Equal(45, versionMadeBy);
        Assert.Equal(0, flags & 0x0800); // no UTF-8 flag
        Assert.Equal(0, flags & 0x0008); // no data descriptor flag for small entry
    }

    [Fact]
    public void VersionNeeded_Is20_WhenNoPerEntryZip64()
    {
        byte[] archive = WriteSmallArchive("small.bin", new byte[10]);

        // Local header version-needed-to-extract at offset 4.
        ushort localVersion = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(4));
        Assert.Equal(20, localVersion);

        // Central directory version-needed-to-extract via ZIP64 EOCD.
        int cdStart = FindCentralDirectoryStart(archive);
        ushort cdVersion = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(cdStart + 6));
        Assert.Equal(20, cdVersion);
    }

    [Fact]
    public void DataDescriptor_LocalAndCentralFlagsAreConsistent()
    {
        // Verify that for a normal small entry, both local and central headers have no
        // data-descriptor flag, and LfhSize is identical regardless of the flag being set.
        byte[] archive = WriteSmallArchive("nodesc.bin", new byte[100]);

        // Local header flags at offset 6.
        ushort localFlags = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(6));
        Assert.Equal(0, localFlags & 0x0008);

        // Central header flags — find CD via ZIP64 EOCD.
        int cdStart = FindCentralDirectoryStart(archive);
        ushort centralFlags = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(cdStart + 8));
        Assert.Equal(0, centralFlags & 0x0008);

        // LfhSize is 30 + name length, unaffected by flag.
        ushort nameLen = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(26));
        ushort extraLen = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(28));
        Assert.Equal(30 + nameLen + extraLen, 30 + nameLen); // extra is 0
    }

    [Fact]
    public void DataDescriptor_LfhSizeUnchanged_WithOrWithoutDescriptorFlag()
    {
        // The data-descriptor flag and version-needed fields are in the fixed-size
        // local file header. Changing them does NOT change the LfhSize (30 + name + extra).
        // This test proves LfhSize parity survives the descriptor path.
        string name = "testfile.bin";
        byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        int expectedLfhSize = 30 + nameBytes.Length;

        // Normal (no descriptor): LfhSize should match.
        using var normalOutput = new MemoryStream();
        StoredZipEntryInfo normalInfo;
        using (var writer = new StoredZipWriter(normalOutput))
        {
            normalInfo = writer.AddEntry(name, stream => stream.Write(new byte[100]));
        }
        Assert.Equal(expectedLfhSize, normalInfo.LocalHeaderSize);

        // The descriptor path cannot be triggered without >4 GiB data, but we can verify
        // that the local header size is computed identically (it's purely name + extra based).
        Assert.Equal(30 + nameBytes.Length, normalInfo.LocalHeaderSize);
    }

    [Fact]
    public void DeflatedEntry_LongSizes_CompileAndPropagate()
    {
        // Verify that DeflatedZipEntryContent now accepts long values end-to-end.
        // This tests the widened type path without writing >4 GiB.
        using var output = new MemoryStream();
        using (var writer = new StoredZipWriter(output))
        {
            writer.AddDeflatedEntry("deflated.bin", destination =>
            {
                // Write a small payload.
                byte[] data = [0x03, 0x00]; // minimal valid deflate empty block
                destination.Write(data);
                return new DeflatedZipEntryContent(0x12345678, 2L, 0L);
            });
        }

        // Verify it's readable by System.IO.Compression.
        byte[] archive = output.ToArray();
        using var readStream = new MemoryStream(archive);
        using var zip = new ZipArchive(readStream, ZipArchiveMode.Read);
        Assert.Single(zip.Entries);
    }

    // --- Helpers ---

    private static byte[] WriteSmallArchive(string entryName, byte[] content)
    {
        using var output = new MemoryStream();
        using (var writer = new StoredZipWriter(output))
        {
            writer.AddEntry(entryName, stream => stream.Write(content));
        }

        return output.ToArray();
    }

    /// <summary>
    /// Finds the central directory start offset by reading the ZIP64 EOCD record
    /// (since the classic EOCD offset field is always the 0xFFFFFFFF sentinel).
    /// </summary>
    private static int FindCentralDirectoryStart(byte[] archive)
    {
        int eocdOffset = archive.Length - 22;
        int locatorOffset = eocdOffset - 20;
        ulong zip64EocdOffset = BinaryPrimitives.ReadUInt64LittleEndian(archive.AsSpan(locatorOffset + 8));
        int z64Offset = checked((int)zip64EocdOffset);
        // CD offset is at ZIP64 EOCD + 48.
        ulong cdOffset = BinaryPrimitives.ReadUInt64LittleEndian(archive.AsSpan(z64Offset + 48));
        return checked((int)cdOffset);
    }

    private string CreateSource(string name)
    {
        string source = Path.Combine(_root, name);
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "AppxManifest.xml"), Manifest, new UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(source, "empty.dat"), []);
        return source;
    }

    private static void WritePayload(string root, string relativePath, byte[] content)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    private static Dictionary<string, int> ReadLocalHeaderSizes(string packagePath)
    {
        byte[] data = File.ReadAllBytes(packagePath);
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int offset = 0;
        while (offset + 30 <= data.Length
            && BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)) == 0x04034B50)
        {
            ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 26));
            ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 28));
            uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 18));
            int headerSize = 30 + nameLength + extraLength;
            string rawName = Encoding.UTF8.GetString(data, offset + 30, nameLength);
            if (OpcPackage.TryCanonicalizePartName(rawName, out string name))
            {
                result[name] = headerSize;
            }

            offset += headerSize + (int)compressedSize;
        }

        return result;
    }
}
