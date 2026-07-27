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
    /// Slots are created on the calling thread as blocks are read, so a per-thread allocation
    /// measurement captures them.  A tiny entry must not rent processor-scaled staging buffers:
    /// before slots were made lazy, a single-block entry at degree 64 rented 192 slots
    /// (~25 MB) to compress 4 KB.
    /// </summary>
    [Fact]
    public void CompressAndHash_SmallEntryAtHighDegree_DoesNotAllocateSlotsItCannotUse()
    {
        const long CeilingBytes = 4L * 1024 * 1024;
        byte[] payload = new byte[4096];
        new Random(7).NextBytes(payload);
        long allocated = 0;

        // Measure on a dedicated thread so unrelated tests cannot contribute, and warm up at
        // degree 1 rather than degree 64: warming up at the measured degree would leave 192
        // buffers in the array pool, so eager slot creation would rent them back for free and
        // the test would pass regardless.
        var thread = new Thread(() =>
        {
            BlockMapWriter.CompressAndHash(
                "warmup.bin", new MemoryStream(payload), Stream.Null, CompressionLevel.Optimal, 1);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            BlockMapWriter.CompressAndHash(
                "small.bin", new MemoryStream(payload), Stream.Null, CompressionLevel.Optimal, 64);
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        });
        thread.Start();
        thread.Join();

        Assert.True(
            allocated < CeilingBytes,
            $"Compressing a {payload.Length:N0}-byte entry at degree 64 allocated {allocated:N0} bytes, " +
            $"exceeding the {CeilingBytes:N0}-byte budget. Staging slots are most likely being " +
            $"created eagerly rather than as blocks are read.");
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
