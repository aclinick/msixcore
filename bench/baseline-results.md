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
- **Extract all parts** — copy every OPC part to a temp directory (equivalent to the deployment
  engine's extraction step; a dedicated `PackageExtractor.Extract` API lands in a later phase).
- **Open loose directory + verify** — `MsixPackage.OpenDirectory` over an unpacked layout + verify.

> A short job (`[SimpleJob(warmupCount: 3, iterationCount: 5)]`) is used so a full run stays under a
> couple of minutes. Numbers are indicative, not publication-grade; the disk-bound `Extract`
> benchmark in particular shows high variance. Re-run on the target machine for authoritative data.

## Snapshot

```

BenchmarkDotNet v0.15.4, Windows 11 (10.0.26300.8935)
Snapdragon X 10-core X1P64100 3.40 GHz (Max: 3.42GHz), 1 CPU, 10 logical and 10 physical cores
.NET SDK 10.0.300
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), Arm64 RyuJIT armv8.0-a
  Job-NTRUNJ : .NET 10.0.8 (10.0.8, 10.0.826.23019), Arm64 RyuJIT armv8.0-a

IterationCount=5  WarmupCount=3

```
| Method                                                        | Mean         | Error         | StdDev       | Gen0      | Gen1    | Allocated  |
|-------------------------------------------------------------- |-------------:|--------------:|-------------:|----------:|--------:|-----------:|
| 'Open + parse manifest/identity (small package, from stream)' |     20.73 us |      2.778 us |     0.722 us |    9.6436 |       - |   39.53 KB |
| 'Open + parse manifest/identity (large package, from file)'   |    329.58 us |     20.501 us |     5.324 us |   23.4375 |  0.4883 |   95.76 KB |
| 'Verify block map (small package)'                            |    147.77 us |     11.475 us |     2.980 us |  103.2715 |  0.2441 |  425.02 KB |
| 'Verify block map (large multi-file/multi-block package)'     | 12,128.86 us |  1,330.353 us |   345.488 us | 1093.7500 | 15.6250 | 4512.92 KB |
| 'Read signature (CMS envelope) from signed package'           |    145.30 us |      3.511 us |     0.912 us |    6.1035 |  0.2441 |   25.83 KB |
| 'Extract all parts to a temp directory (large package)'       | 25,831.08 us | 19,162.815 us | 4,976.522 us |         - |       - |  165.75 KB |
| 'Open loose directory + verify block map (large package)'     | 13,267.82 us |  1,408.840 us |   218.020 us | 1093.7500 | 15.6250 | 4484.52 KB |

## Reading the numbers

- `1 us` = 1 microsecond; `12,128.86 us` ≈ 12.1 ms.
- **Manifest parse** is cheap (tens of µs); package open cost scales with the ZIP central directory
  size, hence the small→large difference.
- **Block-map verification** is dominated by SHA-256 over the payload: ~12 ms for ~10 MB ⇒ on the
  order of ~0.8 GB/s hashing throughput on this Arm64 host. This is the number most worth watching
  when comparing against the C++ implementation.
- **Signature read** cost is the CMS decode + `CheckSignature`, independent of payload size.
- **Extract** is disk-bound and noisy; treat it as an order-of-magnitude figure.
- `Allocated` (managed bytes/op) is the other axis to track for the "size/efficiency" goal — e.g.
  verification allocates ~4.5 MB for the large package, a candidate for future buffer pooling.
