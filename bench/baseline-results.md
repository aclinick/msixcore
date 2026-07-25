# MSIX Core (.NET) — benchmark baseline

This file is a captured snapshot of a BenchmarkDotNet run of `MsixCore.Benchmarks`. It exists so
we can track the performance of the C# port over time and (eventually) compare it against the
original C++ MSIX Core. Regenerate with:

```
dotnet run -c Release --project bench\MsixCore.Benchmarks -- --filter *
```

then copy `BenchmarkDotNet.Artifacts\results\MsixCore.Benchmarks.PackageBenchmarks-report-github.md`
here.

## What is measured

Payloads are synthesized in-process (an OPC ZIP + a matching `AppxBlockMap.xml`, and for the small
package a self-signed CMS `AppxSignature.p7x`). The "large" package carries an ~10 MB payload spread
across 64 files so block-map hashing and extraction throughput are meaningful.

- **Open + parse manifest/identity** — `MsixPackage.Open` then read `Identity`/`DisplayName`.
- **Verify block map** — `MsixPackage.VerifyBlockMap()` (SHA-256 over every 64 KiB block).
- **Read signature** — `MsixPackage.ReadSignature()` (CMS envelope decode + integrity check).
- **Extract package** — `MsixCore.Deployment.PackageExtractor.Extract(package.Opc, dir)` (small and
  large). To isolate extraction, the package is opened in an `[IterationSetup]` and disposed (plus the
  extracted output deleted) in an `[IterationCleanup]`, both outside the measured region, so neither
  `MsixPackage.Open` nor cleanup is timed; BenchmarkDotNet therefore runs these two with
  `InvocationCount=1` (a separate job row below).
- **Open loose directory + verify** — `MsixPackage.OpenDirectory` over an unpacked layout + verify.

> A short job (`[SimpleJob(warmupCount: 3, iterationCount: 5)]`) is used so a full run stays under a
> couple of minutes. Numbers are indicative, not publication-grade; the disk-bound `Extract`
> benchmarks in particular show higher variance. Re-run on the target machine for authoritative data.

## Snapshot

```

BenchmarkDotNet v0.15.4, Windows 11 (10.0.26300.8935)
Snapdragon X 10-core X1P64100 3.40 GHz (Max: 3.42GHz), 1 CPU, 10 logical and 10 physical cores
.NET SDK 10.0.300
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), Arm64 RyuJIT armv8.0-a
  Job-MJLQTR : .NET 10.0.8 (10.0.8, 10.0.826.23019), Arm64 RyuJIT armv8.0-a
  Job-NTRUNJ : .NET 10.0.8 (10.0.8, 10.0.826.23019), Arm64 RyuJIT armv8.0-a

IterationCount=5  WarmupCount=3

```
| Method                                                        | Job        | InvocationCount | Mean         | Error         | StdDev       | Gen0      | Gen1    | Allocated  |
|-------------------------------------------------------------- |----------- |---------------- |-------------:|--------------:|-------------:|----------:|--------:|-----------:|
| 'Extract package payload to a temp directory (small package)' | Job-MJLQTR | 1               |  5,445.82 us |  2,372.591 us |   616.154 us |         - |       - |  200.66 KB |
| 'Extract package payload to a temp directory (large package)' | Job-MJLQTR | 1               | 67,628.36 us | 26,349.618 us | 6,842.912 us |         - |       - |  514.53 KB |
| 'Open + parse manifest/identity (small package, from stream)' | Job-NTRUNJ | Default         |     23.34 us |      1.651 us |     0.429 us |   10.1318 |       - |   41.57 KB |
| 'Open + parse manifest/identity (large package, from file)'   | Job-NTRUNJ | Default         |    370.08 us |     16.339 us |     2.528 us |   29.2969 |       - |  120.26 KB |
| 'Verify block map (small package)'                            | Job-NTRUNJ | Default         |    177.18 us |     12.182 us |     3.164 us |  103.2715 |  0.2441 |  427.09 KB |
| 'Verify block map (large multi-file/multi-block package)'     | Job-NTRUNJ | Default         | 13,499.15 us |    777.242 us |   201.847 us | 1093.7500 | 15.6250 | 4537.42 KB |
| 'Read signature (CMS envelope) from signed package'           | Job-NTRUNJ | Default         |    155.46 us |      1.169 us |     0.181 us |    6.8359 |  0.2441 |   28.16 KB |
| 'Open loose directory + verify block map (large package)'     | Job-NTRUNJ | Default         | 15,919.84 us |  1,091.801 us |   283.537 us | 1093.7500 | 31.2500 | 4484.53 KB |

## Reading the numbers

- `1 us` = 1 microsecond; `12,624.29 us` ≈ 12.6 ms.
- **Manifest parse** is cheap (tens of µs); package open cost scales with the ZIP central directory
  size, hence the small→large difference.
- **Block-map verification** is dominated by SHA-256 over the payload: ~12.6 ms for ~10 MB ⇒ on the
  order of ~0.8 GB/s hashing throughput on this Arm64 host. This is the number most worth watching
  when comparing against the C++ implementation.
- **Signature read** cost is the CMS decode + `CheckSignature`, independent of payload size.
- **Extract** (real `PackageExtractor.Extract`, now timed in isolation — `Open` is excluded) is
  disk-bound and high-variance: ~55–68 ms for the ~10 MB / 67-part package and ~5.4 ms for the small
  package. It streams parts with a pooled 80 KB buffer, so managed allocation stays low
  (~0.2–0.5 MB) regardless of payload size — a good sign for the "size/efficiency" goal. Treat the
  absolute times as order-of-magnitude figures.
- `Allocated` (managed bytes/op) is the other axis to track — e.g. verification allocates ~4.5 MB
  for the large package, a candidate for future buffer pooling.

