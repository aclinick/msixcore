using System.IO.Compression;
using MsixCore.Packaging.Authoring;
using MsixCore.Packaging.Integrity;

namespace MsixCore.Packaging.Tests;

/// <summary>
/// Parallel block compression is a scheduling change only: MSIX blocks are compressed
/// independently and sync-flushed, so concurrency must never alter a single output byte.
/// These tests pin that equivalence rather than merely asserting the parallel path runs.
/// </summary>
public sealed class ParallelCompressionTests : IDisposable
{
    private const string Manifest =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
          <Identity Name="Contoso.Parallel" Publisher="CN=Contoso" Version="1.0.0.0" ProcessorArchitecture="x64" />
          <Properties>
            <DisplayName>Parallel package</DisplayName>
            <PublisherDisplayName>Contoso Ltd</PublisherDisplayName>
          </Properties>
        </Package>
        """;

    private readonly string _root;

    public ParallelCompressionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msix-parallel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [InlineData("empty", 2)]
    [InlineData("empty", 8)]
    [InlineData("partial", 2)]
    [InlineData("partial", 8)]
    [InlineData("exact-block", 2)]
    [InlineData("exact-block", 8)]
    [InlineData("compressible", 2)]
    [InlineData("compressible", 3)]
    [InlineData("compressible", 8)]
    [InlineData("incompressible", 2)]
    [InlineData("incompressible", 3)]
    [InlineData("incompressible", 8)]
    [InlineData("mixed", 2)]
    [InlineData("mixed", 8)]
    [InlineData("mixed", 64)]
    public void CompressAndHash_ParallelAndSequential_ProduceIdenticalOutput(string shape, int degree)
    {
        byte[] payload = CreatePayload(shape);

        AssertEquivalent(payload, degree);
    }

    /// <summary>
    /// The reader fills <c>degree * 3</c> slots per batch and stops the outer loop only when a
    /// batch comes back short.  Payloads sized exactly on, one past, and at twice the batch
    /// boundary exercise the drain, the refill, and the zero-length final batch.
    /// </summary>
    [Theory]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(2, -1)]
    [InlineData(4, 0)]
    [InlineData(4, 1)]
    [InlineData(4, -1)]
    public void CompressAndHash_AtSlotBatchBoundaries_ProducesIdenticalOutput(int degree, int blockDelta)
    {
        int blocks = (degree * 3) + blockDelta;
        byte[] payload = CreateCompressiblePayload(blocks * BlockMap.BlockSize);

        CompressedBlockMapFile parallel = AssertEquivalent(payload, degree);

        Assert.Equal(blocks, parallel.File.Blocks.Count);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    public void CompressAndHash_ParallelOutput_InflatesBackToSource(int degree)
    {
        byte[] payload = CreatePayload("mixed");
        using var compressed = new MemoryStream();

        CompressedBlockMapFile result = BlockMapWriter.CompressAndHash(
            "data.bin",
            new MemoryStream(payload),
            compressed,
            CompressionLevel.Optimal,
            degree);

        // Every block must inflate independently — that property is what makes MSIX blocks
        // restartable, and it is the one a batching bug would silently break.
        byte[] compressedBytes = compressed.ToArray();
        using var inflated = new MemoryStream();
        int offset = 0;
        foreach (BlockMapBlock block in result.File.Blocks)
        {
            int length = (int)block.CompressedSize!.Value;
            using var blockStream = new MemoryStream(compressedBytes, offset, length, writable: false);
            using var deflate = new DeflateStream(blockStream, CompressionMode.Decompress);
            deflate.CopyTo(inflated);
            offset += length;
        }

        Assert.Equal(payload, inflated.ToArray());
        Assert.Equal(payload.Length, result.UncompressedSize);
    }

    [Fact]
    public void Build_SequentialAndParallel_ProduceByteIdenticalPackages()
    {
        string source = CreateSource();
        string sequential = Path.Combine(_root, "sequential.msix");
        string parallel = Path.Combine(_root, "parallel.msix");

        MsixPackageBuilder.Build(
            source,
            sequential,
            new PackOptions { CompressionLevel = CompressionLevel.Optimal, MaxDegreeOfParallelism = 1 });
        MsixPackageBuilder.Build(
            source,
            parallel,
            new PackOptions { CompressionLevel = CompressionLevel.Optimal, MaxDegreeOfParallelism = 8 });

        Assert.Equal(File.ReadAllBytes(sequential), File.ReadAllBytes(parallel));

        using MsixPackage package = MsixPackage.Open(parallel);
        Assert.True(package.VerifyBlockMap().IsValid);
    }

    [Fact]
    public void Build_NegativeMaxDegreeOfParallelism_Throws()
    {
        string source = CreateSource();

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => MsixPackageBuilder.Build(
                source,
                Path.Combine(_root, "negative.msix"),
                new PackOptions { CompressionLevel = CompressionLevel.Optimal, MaxDegreeOfParallelism = -1 }));

        Assert.Contains("MaxDegreeOfParallelism", exception.Message, StringComparison.Ordinal);
    }

    private static CompressedBlockMapFile AssertEquivalent(byte[] payload, int degree)
    {
        using var sequentialOutput = new MemoryStream();
        using var parallelOutput = new MemoryStream();

        CompressedBlockMapFile sequential = BlockMapWriter.CompressAndHash(
            "data.bin",
            new MemoryStream(payload),
            sequentialOutput,
            CompressionLevel.Optimal,
            maxDegreeOfParallelism: 1);
        CompressedBlockMapFile parallel = BlockMapWriter.CompressAndHash(
            "data.bin",
            new MemoryStream(payload),
            parallelOutput,
            CompressionLevel.Optimal,
            degree);

        Assert.Equal(sequentialOutput.ToArray(), parallelOutput.ToArray());
        Assert.Equal(sequential.Crc32, parallel.Crc32);
        Assert.Equal(sequential.CompressedSize, parallel.CompressedSize);
        Assert.Equal(sequential.UncompressedSize, parallel.UncompressedSize);
        Assert.Equal(sequential.File.Size, parallel.File.Size);
        Assert.Equal(
            sequential.File.Blocks.Select(static block => block.Hash),
            parallel.File.Blocks.Select(static block => block.Hash));
        Assert.Equal(
            sequential.File.Blocks.Select(static block => block.CompressedSize),
            parallel.File.Blocks.Select(static block => block.CompressedSize));

        return parallel;
    }

    private static byte[] CreatePayload(string shape) => shape switch
    {
        "empty" => [],
        "partial" => CreateCompressiblePayload(1234),
        "exact-block" => CreateCompressiblePayload(BlockMap.BlockSize),
        "compressible" => CreateCompressiblePayload((BlockMap.BlockSize * 5) + 77),
        "incompressible" => CreateIncompressiblePayload((BlockMap.BlockSize * 5) + 77),
        "mixed" => [.. CreateCompressiblePayload((BlockMap.BlockSize * 2) + 11), .. CreateIncompressiblePayload((BlockMap.BlockSize * 3) + 29)],
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown payload shape."),
    };

    private static byte[] CreateCompressiblePayload(int length) =>
        [.. Enumerable.Range(0, length).Select(static value => (byte)('A' + (value % 7)))];

    private static byte[] CreateIncompressiblePayload(int length)
    {
        byte[] buffer = new byte[length];
        new Random(20260214).NextBytes(buffer);
        return buffer;
    }

    private string CreateSource()
    {
        string source = Path.Combine(_root, "source");
        Directory.CreateDirectory(Path.Combine(source, "Data"));
        File.WriteAllText(Path.Combine(source, "AppxManifest.xml"), Manifest);
        File.WriteAllBytes(
            Path.Combine(source, "Data", "compressible.bin"),
            CreateCompressiblePayload((BlockMap.BlockSize * 6) + 101));
        File.WriteAllBytes(
            Path.Combine(source, "Data", "incompressible.bin"),
            CreateIncompressiblePayload((BlockMap.BlockSize * 4) + 53));
        File.WriteAllBytes(Path.Combine(source, "Data", "empty.dat"), []);
        return source;
    }
}
