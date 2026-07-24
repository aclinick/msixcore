using System.Text;
using MsixCore.Packaging.Integrity;
using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging.Tests;

public class BlockMapVerifierTests
{
    private static Dictionary<string, byte[]> SampleParts()
    {
        // A small text file and a >64 KiB file to exercise multi-block hashing.
        byte[] big = new byte[BlockMap.BlockSize + 1234];
        for (int i = 0; i < big.Length; i++)
        {
            big[i] = (byte)(i * 31);
        }

        return new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["AppxManifest.xml"] = Encoding.UTF8.GetBytes("<Package/>"),
            ["Assets/Logo.png"] = big,
        };
    }

    [Fact]
    public void Verify_MatchingPackage_IsValid()
    {
        Dictionary<string, byte[]> parts = SampleParts();
        using OpcPackage opc = PackageBuilder.OpcFrom(parts);
        BlockMap map = PackageBuilder.BlockMapFor(parts);

        BlockMapVerificationResult result = BlockMapVerifier.Verify(opc, map);

        Assert.True(result.IsValid);
        Assert.All(result.Files, f => Assert.True(f.IsValid));
        Assert.Empty(result.CoverageErrors);
    }

    [Fact]
    public void Verify_MultiBlockFile_HasCorrectBlockCount()
    {
        Dictionary<string, byte[]> parts = SampleParts();
        BlockMap map = PackageBuilder.BlockMapFor(parts);

        BlockMapFile logo = map.Files.Single(f => f.Name == "Assets/Logo.png");
        Assert.Equal(2, logo.Blocks.Count);
    }

    [Fact]
    public void Verify_TamperedContent_FailsBlockHash()
    {
        Dictionary<string, byte[]> parts = SampleParts();
        BlockMap map = PackageBuilder.BlockMapFor(parts);

        // Package content differs from what the block map was computed over.
        parts["AppxManifest.xml"] = Encoding.UTF8.GetBytes("<Package tampered=\"1\"/>");
        using OpcPackage opc = PackageBuilder.OpcFrom(parts);

        BlockMapVerificationResult result = BlockMapVerifier.Verify(opc, map);

        Assert.False(result.IsValid);
        BlockMapFileResult manifest = result.Files.Single(f => f.Name == "AppxManifest.xml");
        Assert.False(manifest.IsValid);
        Assert.Contains("hash mismatch", manifest.Error);
    }

    [Fact]
    public void Verify_SizeMismatch_Fails()
    {
        Dictionary<string, byte[]> parts = SampleParts();
        BlockMap map = PackageBuilder.BlockMapFor(parts);

        // Same block boundaries but overstate the declared size.
        BlockMapFile original = map.Files[0];
        var tampered = original with { Size = original.Size + 100 };
        map = map with { Files = new List<BlockMapFile> { tampered, map.Files[1] } };

        using OpcPackage opc = PackageBuilder.OpcFrom(parts);
        BlockMapVerificationResult result = BlockMapVerifier.Verify(opc, map);

        Assert.False(result.IsValid);
        Assert.Contains("size mismatch", result.Files[0].Error);
    }

    [Fact]
    public void Verify_PackagePartNotInBlockMap_ReportsCoverageError()
    {
        Dictionary<string, byte[]> parts = SampleParts();
        BlockMap map = PackageBuilder.BlockMapFor(parts);

        // Add an extra payload part not covered by the block map.
        parts["Extra.dll"] = Encoding.UTF8.GetBytes("payload");
        using OpcPackage opc = PackageBuilder.OpcFrom(parts);

        BlockMapVerificationResult result = BlockMapVerifier.Verify(opc, map);

        Assert.False(result.IsValid);
        Assert.Contains(result.CoverageErrors, e => e.Contains("Extra.dll") && e.Contains("not covered"));
    }

    [Fact]
    public void Verify_BlockMapFileMissingFromPackage_Fails()
    {
        Dictionary<string, byte[]> parts = SampleParts();
        BlockMap map = PackageBuilder.BlockMapFor(parts);

        // Package omits a file the block map declares.
        parts.Remove("Assets/Logo.png");
        using OpcPackage opc = PackageBuilder.OpcFrom(parts);

        BlockMapVerificationResult result = BlockMapVerifier.Verify(opc, map);

        Assert.False(result.IsValid);
        BlockMapFileResult logo = result.Files.Single(f => f.Name == "Assets/Logo.png");
        Assert.False(logo.IsValid);
        Assert.Contains("missing from the package", logo.Error);
    }

    [Fact]
    public void Verify_ExcludedParts_AreNotCoverageErrors()
    {
        Dictionary<string, byte[]> parts = SampleParts();
        BlockMap map = PackageBuilder.BlockMapFor(parts);

        // The three special OPC parts must never count against coverage.
        parts[OpcPartNames.AppxBlockMap] = Encoding.UTF8.GetBytes("<BlockMap/>");
        parts[OpcPartNames.AppxSignature] = new byte[] { 1, 2, 3 };
        parts[OpcPartNames.ContentTypes] = Encoding.UTF8.GetBytes("<Types/>");
        using OpcPackage opc = PackageBuilder.OpcFrom(parts);

        BlockMapVerificationResult result = BlockMapVerifier.Verify(opc, map);

        Assert.True(result.IsValid);
        Assert.Empty(result.CoverageErrors);
    }
}
