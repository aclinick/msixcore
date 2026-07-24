using System.Text;
using MsixCore.Packaging.Integrity;

namespace MsixCore.Packaging.Tests;

public class BlockMapParserTests
{
    private const string SampleBlockMap =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <BlockMap xmlns="http://schemas.microsoft.com/appx/2010/blockmap" HashMethod="http://www.w3.org/2001/04/xmlenc#sha256">
          <File Name="AppxManifest.xml" Size="1024" LfhSize="57">
            <Block Hash="AAAA" />
          </File>
          <File Name="Assets\Logo.png" Size="131072" LfhSize="49">
            <Block Hash="BBBB" Size="500" />
            <Block Hash="CCCC" Size="400" />
          </File>
          <File Name="Empty.txt" Size="0" LfhSize="40" />
        </BlockMap>
        """;

    private static BlockMap ParseSample() =>
        BlockMapParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(SampleBlockMap)));

    [Fact]
    public void Parse_ReadsHashMethodAndFiles()
    {
        BlockMap map = ParseSample();

        Assert.Equal(BlockMapHashMethod.Sha256, map.HashMethod);
        Assert.Equal(3, map.Files.Count);
    }

    [Fact]
    public void Parse_NormalizesBackslashesToForwardSlashes()
    {
        BlockMap map = ParseSample();

        Assert.Equal("Assets/Logo.png", map.Files[1].Name);
    }

    [Fact]
    public void Parse_ReadsBlocksAndCompressedSize()
    {
        BlockMap map = ParseSample();

        BlockMapFile logo = map.Files[1];
        Assert.Equal(131072, logo.Size);
        Assert.Equal(2, logo.Blocks.Count);
        Assert.Equal("BBBB", logo.Blocks[0].Hash);
        Assert.Equal(500, logo.Blocks[0].CompressedSize);
    }

    [Fact]
    public void Parse_UncompressedBlock_HasNullCompressedSize()
    {
        BlockMap map = ParseSample();

        Assert.Null(map.Files[0].Blocks[0].CompressedSize);
    }

    [Fact]
    public void Parse_EmptyFile_HasNoBlocks()
    {
        BlockMap map = ParseSample();

        Assert.Empty(map.Files[2].Blocks);
        Assert.Equal(0, map.Files[2].Size);
    }

    [Fact]
    public void Parse_DefaultHashMethod_IsSha256()
    {
        const string xml = """<BlockMap xmlns="http://schemas.microsoft.com/appx/2010/blockmap" />""";
        BlockMap map = BlockMapParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
        Assert.Equal(BlockMapHashMethod.Sha256, map.HashMethod);
    }

    [Theory]
    [InlineData("http://www.w3.org/2001/04/xmlenc#sha384", BlockMapHashMethod.Sha384)]
    [InlineData("http://www.w3.org/2001/04/xmlenc#sha512", BlockMapHashMethod.Sha512)]
    public void ParseHashMethod_MapsSupportedAlgorithms(string uri, BlockMapHashMethod expected)
    {
        Assert.Equal(expected, BlockMapParser.ParseHashMethod(uri));
    }

    [Fact]
    public void Parse_UnsupportedHashMethod_Throws()
    {
        const string xml = """<BlockMap HashMethod="http://www.w3.org/2001/04/xmlenc#md5" />""";
        Assert.Throws<InvalidDataException>(() => BlockMapParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
    }

    [Fact]
    public void Parse_FileMissingName_Throws()
    {
        const string xml = """<BlockMap><File Size="1"><Block Hash="AA" /></File></BlockMap>""";
        Assert.Throws<InvalidDataException>(() => BlockMapParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
    }

    [Fact]
    public void Parse_BlockMissingHash_Throws()
    {
        const string xml = """<BlockMap><File Name="a" Size="1"><Block /></File></BlockMap>""";
        Assert.Throws<InvalidDataException>(() => BlockMapParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
    }

    [Fact]
    public void Parse_NegativeSize_Throws()
    {
        const string xml = """<BlockMap><File Name="a" Size="-1"><Block Hash="AA" /></File></BlockMap>""";
        Assert.Throws<InvalidDataException>(() => BlockMapParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
    }

    [Fact]
    public void Parse_WrongRoot_Throws()
    {
        const string xml = """<NotABlockMap />""";
        Assert.Throws<InvalidDataException>(() => BlockMapParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
    }

    [Fact]
    public void Parse_RejectsDtd()
    {
        const string xml =
            """
            <?xml version="1.0"?>
            <!DOCTYPE BlockMap [ <!ENTITY xxe "boom"> ]>
            <BlockMap />
            """;
        Assert.Throws<InvalidDataException>(() => BlockMapParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
    }
}
