using BenchmarkDotNet.Attributes;
using MsixCore.Deployment;
using MsixCore.Packaging;
using MsixCore.Packaging.Integrity;

namespace MsixCore.Benchmarks;

/// <summary>
/// Performance benchmarks over the MSIX Core packaging/deployment surface: package open + manifest
/// parse, block-map verification, signature read, extraction, and loose-directory verification.
/// </summary>
/// <remarks>
/// Payloads are synthesized once in <see cref="GlobalSetup"/>. The "large" package carries an
/// ~10&#160;MB payload across many files so block-map hashing and extraction throughput are
/// meaningful. A short <see cref="SimpleJobAttribute"/> keeps a full run tractable while remaining
/// statistically usable.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class PackageBenchmarks
{
    private const long LargePayloadBytes = 10L * 1024 * 1024;
    private const int LargeFileCount = 64;

    private string _workRoot = string.Empty;
    private string _extractRootSmall = string.Empty;
    private string _extractRootLarge = string.Empty;
    private byte[] _smallPackageBytes = [];
    private byte[] _largePackageBytes = [];
    private string _smallPackagePath = string.Empty;
    private string _largePackagePath = string.Empty;
    private string _looseDir = string.Empty;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _workRoot = Path.Combine(Path.GetTempPath(), "msixcore-bench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workRoot);
        _extractRootSmall = Path.Combine(_workRoot, "extract-small");
        _extractRootLarge = Path.Combine(_workRoot, "extract-large");

        Dictionary<string, byte[]> smallPayload = SyntheticPackage.BuildPayload(fileCount: 4, totalPayloadBytes: 64 * 1024);
        Dictionary<string, byte[]> largePayload = SyntheticPackage.BuildPayload(LargeFileCount, LargePayloadBytes);

        using (MemoryStream small = SyntheticPackage.ToZipStream(smallPayload, signed: true))
        {
            _smallPackageBytes = small.ToArray();
        }

        using (MemoryStream large = SyntheticPackage.ToZipStream(largePayload, signed: false))
        {
            _largePackageBytes = large.ToArray();
        }

        _smallPackagePath = SyntheticPackage.ToFile(_workRoot, "small.msix", smallPayload, signed: true);
        _largePackagePath = SyntheticPackage.ToFile(_workRoot, "large.msix", largePayload, signed: false);
        _looseDir = SyntheticPackage.ToLooseDirectory(Path.Combine(_workRoot, "loose"), largePayload);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        try
        {
            if (Directory.Exists(_workRoot))
            {
                Directory.Delete(_workRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of the temp working tree.
        }
    }

    [IterationCleanup(Targets = [nameof(ExtractPackageSmall), nameof(ExtractPackageLarge)])]
    public void CleanupExtractDirs()
    {
        // Runs once per iteration, OUTSIDE the measured region, so deleting the extracted output is
        // never timed. Within an iteration the benchmark simply overwrites the same files.
        DeleteBestEffort(_extractRootSmall);
        DeleteBestEffort(_extractRootLarge);
    }

    [Benchmark(Description = "Open + parse manifest/identity (small package, from stream)")]
    public string OpenAndParseManifestSmall()
    {
        using var stream = new MemoryStream(_smallPackageBytes, writable: false);
        using MsixPackage package = MsixPackage.Open(stream, leaveOpen: true);
        return package.Identity.PackageFullName + package.DisplayName;
    }

    [Benchmark(Description = "Open + parse manifest/identity (large package, from file)")]
    public string OpenAndParseManifestLarge()
    {
        using MsixPackage package = MsixPackage.Open(_largePackagePath);
        return package.Identity.PackageFullName + package.DisplayName;
    }

    [Benchmark(Description = "Verify block map (small package)")]
    public bool VerifyBlockMapSmall()
    {
        using var stream = new MemoryStream(_smallPackageBytes, writable: false);
        using MsixPackage package = MsixPackage.Open(stream, leaveOpen: true);
        return package.VerifyBlockMap().IsValid;
    }

    [Benchmark(Description = "Verify block map (large multi-file/multi-block package)")]
    public bool VerifyBlockMapLarge()
    {
        using MsixPackage package = MsixPackage.Open(_largePackagePath);
        return package.VerifyBlockMap().IsValid;
    }

    [Benchmark(Description = "Read signature (CMS envelope) from signed package")]
    public bool ReadSignatureSmall()
    {
        using var stream = new MemoryStream(_smallPackageBytes, writable: false);
        using MsixPackage package = MsixPackage.Open(stream, leaveOpen: true);
        PackageSignature? signature = package.ReadSignature();
        return signature?.IsCmsIntegrityValid ?? false;
    }

    [Benchmark(Description = "Extract package to a temp directory (small package)")]
    public void ExtractPackageSmall()
    {
        using var stream = new MemoryStream(_smallPackageBytes, writable: false);
        using MsixPackage package = MsixPackage.Open(stream, leaveOpen: true);
        PackageExtractor.Extract(package.Opc, _extractRootSmall);
    }

    [Benchmark(Description = "Extract package to a temp directory (large package)")]
    public void ExtractPackageLarge()
    {
        using MsixPackage package = MsixPackage.Open(_largePackagePath);
        PackageExtractor.Extract(package.Opc, _extractRootLarge);
    }

    [Benchmark(Description = "Open loose directory + verify block map (large package)")]
    public bool DirectoryOpenAndVerifyLarge()
    {
        using MsixPackage package = MsixPackage.OpenDirectory(_looseDir);
        return package.VerifyBlockMap().IsValid;
    }

    private static void DeleteBestEffort(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort: leftover extract output is cleaned with the work root in GlobalCleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }
}

