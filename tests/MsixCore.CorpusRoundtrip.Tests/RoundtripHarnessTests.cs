using System.IO.Compression;
using System.Text;
using MsixCore.CorpusRoundtrip;
using MsixCore.Packaging.Authoring;

namespace MsixCore.CorpusRoundtrip.Tests;

public sealed class RoundtripHarnessTests : IDisposable
{
    private const string Manifest =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
          <Identity Name="Contoso.Roundtrip" Publisher="CN=Contoso" Version="1.0.0.0" ProcessorArchitecture="x64" />
          <Properties>
            <DisplayName>Roundtrip</DisplayName>
            <PublisherDisplayName>Contoso</PublisherDisplayName>
            <Logo>Assets\logo.png</Logo>
          </Properties>
          <Dependencies>
            <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.26100.0" />
          </Dependencies>
        </Package>
        """;

    private readonly string _root;

    public RoundtripHarnessTests()
    {
        _root = Path.Combine(AppContext.BaseDirectory, "roundtrip-test-scratch-" + Guid.NewGuid().ToString("N"));
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
    public void Normalize_SameInput_ProducesDeterministicPayloadAndTimestamps()
    {
        string source = CreateSource("source");
        File.WriteAllText(Path.Combine(source, "AppxBlockMap.xml"), "<generated/>");
        File.WriteAllText(Path.Combine(source, "AppxSignature.p7x"), "signature");
        string first = Path.Combine(_root, "first");
        string second = Path.Combine(_root, "second");

        SourceNormalizer.Normalize(source, first);
        SourceNormalizer.Normalize(source, second);

        Assert.False(File.Exists(Path.Combine(first, "AppxBlockMap.xml")));
        Assert.False(File.Exists(Path.Combine(first, "AppxSignature.p7x")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(first, "Assets", "data.txt")), File.ReadAllBytes(Path.Combine(second, "Assets", "data.txt")));
        Assert.Equal(SourceNormalizer.FixedTimestamp.UtcDateTime, File.GetLastWriteTimeUtc(Path.Combine(first, "Assets", "data.txt")));
        Assert.Equal(SourceNormalizer.FixedTimestamp.UtcDateTime, File.GetLastWriteTimeUtc(Path.Combine(second, "Assets", "data.txt")));
    }

    [Fact]
    public void OursStoredPack_IsByteIdenticalAcrossRuns()
    {
        string source = CreateSource("stored-source");
        string first = Path.Combine(_root, "first.msix");
        string second = Path.Combine(_root, "second.msix");

        OurPacker.Pack(source, first, RoundtripMode.Stored);
        OurPacker.Pack(source, second, RoundtripMode.Stored);

        Assert.Null(RawByteDiffer.FindFirstDifference(first, second));
    }

    [Fact]
    public void ZipStructuralDiff_ReportsNoDiffForIdenticalArchive()
    {
        string archive = Path.Combine(_root, "archive.zip");
        CreateZip(archive, ("a.txt", "alpha"));

        ZipStructuralDiffResult result = ZipStructuralDiffer.Compare(archive, archive);

        Assert.True(result.IsIdentical);
        Assert.Empty(result.Differences);
        Assert.Null(result.FirstByteDifference);
    }

    [Fact]
    public void ZipStructuralDiff_ReportsEntryMutation()
    {
        string left = Path.Combine(_root, "left.zip");
        string right = Path.Combine(_root, "right.zip");
        CreateZip(left, ("a.txt", "alpha"));
        CreateZip(right, ("a.txt", "bravo"));

        ZipStructuralDiffResult result = ZipStructuralDiffer.Compare(left, right);

        Assert.False(result.IsIdentical);
        Assert.Contains(result.Differences, static difference => difference.Field == "CRC-32");
    }

    [Fact]
    public void ZipStructuralDiff_IgnoresEocdSignatureInsideComment()
    {
        string archive = Path.Combine(_root, "comment.zip");
        byte[] comment = Encoding.ASCII.GetBytes("PK\x05\x06" + new string('x', 48));
        CreateStoredZip(archive, [("a.txt", "alpha")], comment);

        IReadOnlyList<ZipEntryInfo> entries = ZipStructuralDiffer.ReadEntries(archive);
        ZipStructuralDiffResult result = ZipStructuralDiffer.Compare(archive, archive);

        Assert.Single(entries);
        Assert.Equal("a.txt", entries[0].Name);
        Assert.True(result.IsIdentical);
    }

    [Fact]
    public void ZipStructuralDiff_ParsesZip64CentralDirectoryAndExtraFields()
    {
        string archive = Path.Combine(_root, "zip64.zip");
        CreateStoredZip(archive, [("large.txt", "small synthetic payload")], comment: [], useZip64: true);

        ZipEntryInfo entry = Assert.Single(ZipStructuralDiffer.ReadEntries(archive));

        Assert.Equal("large.txt", entry.Name);
        Assert.Equal(23, entry.CompressedSize);
        Assert.Equal(23, entry.UncompressedSize);
        Assert.Equal(45, entry.VersionNeeded);
        Assert.Equal(uint.MaxValue, entry.LocalHeaderCompressedSize32);
        Assert.Contains("0x0001", entry.CentralDirectoryExtraFields, StringComparison.Ordinal);
    }

    [Fact]
    public void ZipStructuralDiff_ParsesDataDescriptorEntries()
    {
        string archive = Path.Combine(_root, "descriptor.zip");
        CreateStoredZip(archive, [("descriptor.txt", "payload")], comment: [], dataDescriptor: true);

        ZipEntryInfo entry = Assert.Single(ZipStructuralDiffer.ReadEntries(archive));

        Assert.Equal("descriptor.txt", entry.Name);
        Assert.Equal(7, entry.CompressedSize);
        Assert.Equal(7, entry.UncompressedSize);
        Assert.Equal(0x0008, entry.GeneralPurposeFlags & 0x0008);
        Assert.Equal(0U, entry.LocalHeaderCrc32);
        Assert.NotEqual(0U, entry.Crc32);
    }

    [Fact]
    public void ZipStructuralDiff_ReportsOrderingWithoutContentDifferences()
    {
        string left = Path.Combine(_root, "order-left.zip");
        string right = Path.Combine(_root, "order-right.zip");
        CreateStoredZip(left, [("a.txt", "same"), ("b.txt", "same")], comment: []);
        CreateStoredZip(right, [("b.txt", "same"), ("a.txt", "same")], comment: []);

        ZipStructuralDiffResult result = ZipStructuralDiffer.Compare(left, right);

        Assert.False(result.IsIdentical);
        Assert.Contains(result.Differences, static difference => difference.Field == "entry order[0]");
        Assert.DoesNotContain(result.Differences, static difference => difference.Field is "CRC-32" or "compressed size" or "uncompressed size");
    }

    [Fact]
    public void BlockMapSemanticDiff_MatchesIdenticalBlockMaps()
    {
        SemanticBlockMap map = ParseBlockMap(lfhSize: 39, hash: "abc=");

        BlockMapSemanticDiffResult result = BlockMapSemanticDiffer.Compare(map, map, includeLfhSizeAndBlockSizes: true);

        Assert.True(result.IsEquivalent);
        Assert.Empty(result.Differences);
    }

    [Fact]
    public void BlockMapSemanticDiff_FlagsLfhSizeAndHashMismatch()
    {
        SemanticBlockMap left = ParseBlockMap(lfhSize: 39, hash: "abc=");
        SemanticBlockMap right = ParseBlockMap(lfhSize: 41, hash: "def=");

        BlockMapSemanticDiffResult result = BlockMapSemanticDiffer.Compare(left, right, includeLfhSizeAndBlockSizes: true);

        Assert.False(result.IsEquivalent);
        Assert.Contains(result.Differences, static difference => difference.Field == "LfhSize");
        Assert.Contains(result.Differences, static difference => difference.Field == "Block[0].Hash");
    }

    [Fact]
    public void MakeAppxStoredDiff_RunsForTinyPackageWhenAvailable()
    {
        string? makeAppx = MakeAppxLocator.Find();
        if (makeAppx is null)
        {
            return;
        }

        string source = CreateSource("makeappx-source");
        string oursPath = Path.Combine(_root, "ours.msix");
        string makeAppxPath = Path.Combine(_root, "makeappx.msix");
        OurPacker.Pack(source, oursPath, RoundtripMode.Stored);
        ToolOutcome outcome = new MakeAppxRunner(makeAppx).Pack(source, makeAppxPath, RoundtripMode.Stored);

        Assert.True(outcome.Succeeded, outcome.Message);
        ZipStructuralDiffResult result = ZipStructuralDiffer.Compare(oursPath, makeAppxPath);
        Assert.NotNull(result);
    }

    private string CreateSource(string name)
    {
        string source = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(source, "Assets"));
        File.WriteAllText(Path.Combine(source, "AppxManifest.xml"), Manifest);
        File.WriteAllText(Path.Combine(source, "Assets", "data.txt"), "tiny payload");
        File.WriteAllBytes(Path.Combine(source, "Assets", "logo.png"), [0x89, 0x50, 0x4E, 0x47]);
        File.SetLastWriteTimeUtc(Path.Combine(source, "AppxManifest.xml"), SourceNormalizer.FixedTimestamp.UtcDateTime);
        File.SetLastWriteTimeUtc(Path.Combine(source, "Assets", "data.txt"), SourceNormalizer.FixedTimestamp.UtcDateTime);
        File.SetLastWriteTimeUtc(Path.Combine(source, "Assets", "logo.png"), SourceNormalizer.FixedTimestamp.UtcDateTime);
        return source;
    }

    private static void CreateZip(string path, params (string Name, string Content)[] entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, string content) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
            using Stream stream = entry.Open();
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            stream.Write(bytes);
        }
    }

    private static void CreateStoredZip(
        string path,
        IReadOnlyList<(string Name, string Content)> entries,
        byte[] comment,
        bool useZip64 = false,
        bool dataDescriptor = false)
    {
        using FileStream stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        var centralEntries = new List<CentralEntry>();
        foreach ((string name, string content) in entries)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            byte[] contentBytes = Encoding.UTF8.GetBytes(content);
            byte[] localExtra = useZip64 ? Zip64Extra(contentBytes.Length, contentBytes.Length) : [];
            long localOffset = stream.Position;
            uint crc32 = ComputeCrc32(contentBytes);
            writer.Write(0x04034B50U);
            writer.Write((ushort)(useZip64 ? 45 : 20));
            writer.Write((ushort)(0x0800 | (dataDescriptor ? 0x0008 : 0)));
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write(dataDescriptor ? 0U : crc32);
            writer.Write(useZip64 ? uint.MaxValue : dataDescriptor ? 0U : (uint)contentBytes.Length);
            writer.Write(useZip64 ? uint.MaxValue : dataDescriptor ? 0U : (uint)contentBytes.Length);
            writer.Write((ushort)nameBytes.Length);
            writer.Write((ushort)localExtra.Length);
            writer.Write(nameBytes);
            writer.Write(localExtra);
            writer.Write(contentBytes);
            if (dataDescriptor)
            {
                writer.Write(0x08074B50U);
                writer.Write(crc32);
                writer.Write((uint)contentBytes.Length);
                writer.Write((uint)contentBytes.Length);
            }

            centralEntries.Add(new CentralEntry(nameBytes, crc32, contentBytes.Length, localOffset));
        }

        long centralDirectoryOffset = stream.Position;
        foreach (CentralEntry entry in centralEntries)
        {
            byte[] centralExtra = useZip64 ? Zip64Extra(entry.Size, entry.Size, entry.LocalOffset) : [];
            writer.Write(0x02014B50U);
            writer.Write((ushort)(useZip64 ? 45 : 20));
            writer.Write((ushort)(useZip64 ? 45 : 20));
            writer.Write((ushort)(0x0800 | (dataDescriptor ? 0x0008 : 0)));
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write(entry.Crc32);
            writer.Write(useZip64 ? uint.MaxValue : (uint)entry.Size);
            writer.Write(useZip64 ? uint.MaxValue : (uint)entry.Size);
            writer.Write((ushort)entry.NameBytes.Length);
            writer.Write((ushort)centralExtra.Length);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write(0U);
            writer.Write(useZip64 ? uint.MaxValue : (uint)entry.LocalOffset);
            writer.Write(entry.NameBytes);
            writer.Write(centralExtra);
        }

        long centralDirectorySize = stream.Position - centralDirectoryOffset;
        if (useZip64)
        {
            long zip64Offset = stream.Position;
            writer.Write(0x06064B50U);
            writer.Write(44UL);
            writer.Write((ushort)45);
            writer.Write((ushort)45);
            writer.Write(0U);
            writer.Write(0U);
            writer.Write((ulong)centralEntries.Count);
            writer.Write((ulong)centralEntries.Count);
            writer.Write((ulong)centralDirectorySize);
            writer.Write((ulong)centralDirectoryOffset);
            writer.Write(0x07064B50U);
            writer.Write(0U);
            writer.Write((ulong)zip64Offset);
            writer.Write(1U);
        }

        writer.Write(0x06054B50U);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(useZip64 ? ushort.MaxValue : (ushort)centralEntries.Count);
        writer.Write(useZip64 ? ushort.MaxValue : (ushort)centralEntries.Count);
        writer.Write(useZip64 ? uint.MaxValue : (uint)centralDirectorySize);
        writer.Write(useZip64 ? uint.MaxValue : (uint)centralDirectoryOffset);
        writer.Write((ushort)comment.Length);
        writer.Write(comment);
    }

    private static byte[] Zip64Extra(long uncompressedSize, long compressedSize, long? localOffset = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)0x0001);
        writer.Write((ushort)(localOffset.HasValue ? 24 : 16));
        writer.Write((ulong)uncompressedSize);
        writer.Write((ulong)compressedSize);
        if (localOffset.HasValue)
        {
            writer.Write((ulong)localOffset.Value);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> bytes)
    {
        uint state = uint.MaxValue;
        foreach (byte value in bytes)
        {
            state ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                state = (state >> 1) ^ ((state & 1) == 0 ? 0U : 0xEDB88320U);
            }
        }

        return ~state;
    }

    private static SemanticBlockMap ParseBlockMap(int lfhSize, string hash)
    {
        string xml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <BlockMap xmlns="http://schemas.microsoft.com/appx/2010/blockmap" HashMethod="http://www.w3.org/2001/04/xmlenc#sha256">
              <File Name="Assets\data.txt" LfhSize="
            """ + lfhSize.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            """
            " Size="12"><Block Hash="
            """ + hash +
            """
            " /></File>
            </BlockMap>
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return BlockMapSemanticDiffer.Parse(stream);
    }

    private sealed record CentralEntry(byte[] NameBytes, uint Crc32, int Size, long LocalOffset);
}
