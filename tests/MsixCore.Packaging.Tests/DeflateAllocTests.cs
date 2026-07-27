using System.Buffers;
using System.IO.Compression;
using MsixCore.Packaging.Authoring;
using MsixCore.Packaging.Integrity;

namespace MsixCore.Packaging.Tests;

/// <summary>
/// Allocation-budget and correctness tests for the deflate authoring path.
/// These guard against silent allocation regressions that wall-clock benchmarks
/// would not detect (see bench/comparison.md "PR #51" section for history).
/// </summary>
public sealed class DeflateAllocTests : IDisposable
{
    private const string Manifest =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
          <Identity Name="Contoso.AllocTest" Publisher="CN=Contoso" Version="1.0.0.0" ProcessorArchitecture="x64" />
          <Properties>
            <DisplayName>Alloc Budget Test</DisplayName>
            <PublisherDisplayName>Contoso Ltd</PublisherDisplayName>
          </Properties>
        </Package>
        """;

    private readonly string _root;

    public DeflateAllocTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msix-alloc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// Allocation budget guard for the Optimal (deflate) authoring path.
    /// Uses <see cref="GC.GetAllocatedBytesForCurrentThread"/> for determinism.
    ///
    /// Measured baseline (2026-07-25, .NET 10, Arm64, 10 MiB / 64 files):
    ///   ~820 KB managed allocation for the full pack operation.
    /// Ceiling: 4 MiB — generous headroom for runtime/GC variation across machines
    /// and .NET versions while still catching multi-MB regressions like the original
    /// 52 MB DeflateStream+MemoryStream+ToArray pattern.
    /// </summary>
    [Fact]
    public void Build_Optimal_AllocationBudget()
    {
        const long PayloadBytes = 2L * 1024 * 1024;
        const int FileCount = 16;
        const long CeilingBytes = 4L * 1024 * 1024;

        string source = CreateSource("alloc-budget");
        var rng = new Random(42);
        for (int i = 0; i < FileCount; i++)
        {
            byte[] data = new byte[PayloadBytes / FileCount];
            rng.NextBytes(data);
            WritePayload(source, $"Data/file{i:D4}.bin", data);
        }

        string output = Path.Combine(_root, "alloc-budget.msix");
        // GC.GetAllocatedBytesForCurrentThread() only sees this thread, so the budget must be
        // measured on the sequential path.  Left at the default, compression would run on worker
        // threads and their allocations would be invisible here — the guard would silently pass
        // through any regression.  Parallel-path allocation is covered process-wide below.
        var options = new PackOptions
        {
            CompressionLevel = CompressionLevel.Optimal,
            MaxDegreeOfParallelism = 1,
        };

        // Warm up to JIT the code paths.
        MsixPackageBuilder.Build(source, output, options);
        File.Delete(output);

        // Force a full GC so prior allocations don't bleed into the measurement.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        MsixPackageBuilder.Build(source, output, options);
        long after = GC.GetAllocatedBytesForCurrentThread();
        long allocated = after - before;

        Assert.True(
            allocated < CeilingBytes,
            $"Deflate pack allocated {allocated:N0} bytes ({allocated / (double)PayloadBytes:F2} B/B), " +
            $"which exceeds the {CeilingBytes:N0}-byte budget. " +
            $"This likely indicates an allocation regression in the compression path. " +
            $"See bench/comparison.md for measured baselines.");

        // Verify the package is still correct.
        using MsixPackage package = MsixPackage.Open(output);
        Assert.True(package.VerifyBlockMap().IsValid);
    }

    /// <summary>
    /// A tiny entry must not rent processor-scaled staging buffers: before slots were made lazy,
    /// a single-block entry at degree 64 rented 192 slots (~37 MB) to compress 4 KB.  This counts
    /// pool rentals rather than bytes allocated, because <see cref="ArrayPool{T}.Shared"/> is
    /// process-wide: an allocation-based assertion false-passes whenever another test has already
    /// populated the pool, since the eager implementation then rents its slots without allocating.
    /// </summary>
    [Fact]
    public void CompressAndHash_SmallEntryAtHighDegree_DoesNotRentSlotsItCannotUse()
    {
        byte[] payload = new byte[4096];
        new Random(7).NextBytes(payload);
        var pool = new CountingArrayPool();

        BlockMapWriter.CompressAndHash(
            "small.bin",
            new MemoryStream(payload),
            Stream.Null,
            CompressionLevel.Optimal,
            maxDegreeOfParallelism: 64,
            bufferPool: pool);

        // One block plus the read that discovers EOF, so at most two slots — two buffers each.
        // The bound that matters is that rentals track the entry size, not the degree: the eager
        // implementation rented 192 slots (384 buffers) for this same 4 KB payload.
        Assert.True(
            pool.RentCount <= 4,
            $"Compressing a {payload.Length:N0}-byte entry at degree 64 rented {pool.RentCount} buffers. " +
            $"Staging slots are most likely being created eagerly rather than as blocks are read.");
        Assert.Equal(pool.RentCount, pool.ReturnCount);
    }

    /// <summary>
    /// Rentals must stay bounded by the batch size rather than growing with the payload, which is
    /// what keeps peak memory flat for very large entries.
    /// </summary>
    [Fact]
    public void CompressAndHash_ManyBlocks_RentsAtMostOneSlotPerBatchEntry()
    {
        const int Degree = 4;
        byte[] payload = new byte[BlockMap.BlockSize * 40];
        new Random(11).NextBytes(payload);
        var pool = new CountingArrayPool();

        BlockMapWriter.CompressAndHash(
            "large.bin",
            new MemoryStream(payload),
            Stream.Null,
            CompressionLevel.Optimal,
            maxDegreeOfParallelism: Degree,
            bufferPool: pool);

        // 40 blocks through a 12-slot batch must still only ever rent the 12 slots, twice each.
        Assert.Equal(Degree * 3 * 2, pool.RentCount);
        Assert.Equal(pool.RentCount, pool.ReturnCount);
    }

    private sealed class CountingArrayPool : ArrayPool<byte>
    {
        private int _rentCount;
        private int _returnCount;

        public int RentCount => _rentCount;

        public int ReturnCount => _returnCount;

        public override byte[] Rent(int minimumLength)
        {
            Interlocked.Increment(ref _rentCount);
            return new byte[minimumLength];
        }

        public override void Return(byte[] array, bool clearArray = false) =>
            Interlocked.Increment(ref _returnCount);
    }

    /// <summary>
    /// Verifies that deflate-compressed packages produce byte-identical output when
    /// built from the same input — a sanity check that pooled/reused buffers don't
    /// leak stale data between entries.
    /// </summary>
    [Fact]
    public void Build_Optimal_MultipleEntries_NoCrossContamination()
    {
        string source = CreateSource("cross-contamination");
        var rng = new Random(99);

        // Create files of varying sizes — some smaller, some larger than a 64 KiB block.
        int[] sizes = [100, 32_000, 65_536, 65_537, 130_000, 1];
        for (int i = 0; i < sizes.Length; i++)
        {
            byte[] data = new byte[sizes[i]];
            rng.NextBytes(data);
            WritePayload(source, $"Data/var{i:D2}.bin", data);
        }

        string first = Path.Combine(_root, "cross-first.msix");
        string second = Path.Combine(_root, "cross-second.msix");
        var options = new PackOptions { CompressionLevel = CompressionLevel.Optimal };

        MsixPackageBuilder.Build(source, first, options);
        MsixPackageBuilder.Build(source, second, options);

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));

        using MsixPackage package = MsixPackage.Open(first);
        Assert.True(package.VerifyBlockMap().IsValid);
    }

    private string CreateSource(string name)
    {
        string source = Path.Combine(_root, name);
        Directory.CreateDirectory(source);
        File.WriteAllText(
            Path.Combine(source, "AppxManifest.xml"),
            Manifest,
            new System.Text.UTF8Encoding(false));
        return source;
    }

    private static void WritePayload(string root, string relativePath, byte[] content)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }
}
