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
}
