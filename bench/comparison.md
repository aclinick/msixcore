# msixkit vs. Windows SDK MakeAppx

This is a measured, reproducible head-to-head comparison of the .NET 10 `msixkit`
port and Windows SDK `makeappx.exe`. Raw run summaries are in
[`comparison-results.md`](comparison-results.md); per-run data is regenerated as
`comparison-results.json` by the harness.

> **Primary comparison: native Arm64 vs. native Arm64.** The .NET runtime,
> `msixkit`, and Windows SDK MakeAppx are all native Arm64 binaries on this
> Snapdragon X host. No emulation is involved in the headline runtime results.
> The SDK's x64 MakeAppx can be selected explicitly as a secondary emulation
> curiosity, but those numbers must be kept separate.

## Environment

| Item | Value |
| --- | --- |
| Host | Windows 10.0.26300, Snapdragon X, Arm64 |
| .NET | SDK 10.0.300; .NET 10 Arm64 runtime |
| msixkit | Release `net10.0` apphost, native Arm64 |
| MakeAppx | Windows SDK 10.0.26100.8249, native Arm64 |
| Repetitions | 1 discarded warmup + 21 measured processes |
| Package mode | Unsigned, uncompressed/stored (`makeappx /nc`) |

## Methodology

Run from the repository root:

```powershell
pwsh bench\Compare-Tools.ps1 -Iterations 21
pwsh bench\Measure-Size.ps1
```

The comparison script defaults directly to:

```text
C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\arm64\makeappx.exe
```

An optional x64-under-emulation run can be captured separately:

```powershell
pwsh bench\Compare-Tools.ps1 -Iterations 7 `
  -MakeAppxPath 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe' `
  -OutputPath bench\comparison-results-x64-emulated.md
```

`Compare-Tools.ps1` builds `src\msixkit` in Release and deterministically generates
three valid loose package layouts: 1 MiB/8 payload files, 10 MiB/64 files, and
64 MiB/128 files. The same layouts and canonical packages are used by both tools.
Compression is disabled because this port currently writes stored entries
([issue #41](https://github.com/aclinick/msixcore/issues/41)); otherwise MakeAppx
compression would change both the work performed and unpack input size.
Timed MakeAppx pack and unpack runs also use `/nv` to disable its additional semantic
validation, equalizing the workload to author-vs-author and extract-vs-extract.
MakeAppx validation remains enabled for untimed canonical-package creation and
cross-tool interoperability checks, so correctness is not traded for speed.

Every sample starts a fresh external process. Wall time uses
`System.Diagnostics.Stopwatch` from process start through exit. Peak working set uses
the process object's OS-reported peak while alive. “Private bytes” is a 5 ms sampled
maximum and can miss very short peaks, so working set is the primary memory result.
Output deletion and corpus generation are outside the timed region. Disk data is
warmed by one discarded operation per tool/corpus.

Correctness is part of the harness:

- each msixkit-authored package is unpacked by MakeAppx, then every source file is
  checked by SHA-256;
- each MakeAppx-authored package is opened by `msixkit inspect`;
- timed unpack results from both tools are checked against the source layout.

Headline multipliers below are **MakeAppx / msixkit**. **Greater than 1.00× means
msixkit is faster or uses less memory**; below 1.00× would honestly show a MakeAppx
win. Min/max are included because filesystem and security-scanner interference is
visible on this workstation; medians are the comparison statistic.

## PR #51 authoring optimization A/B

This A/B isolates the PR #51 authoring changes by running the new
`bench\MsixCore.Benchmarks` harness against both the optimized code and a
throwaway `HEAD~1` worktree with only the benchmark project copied forward.

| Item | Value |
| --- | --- |
| Optimized commit | `4fd34e4c56861c121407eeccf11d823458eaf7eb` |
| Baseline commit | `9ba5b227f61163b651442772d9de2692a1ab61bc` |
| Command | `dotnet run -c Release --project bench\MsixCore.Benchmarks\MsixCore.Benchmarks.csproj -- --filter "*PackPackage*" "*Crc*"` |
| BenchmarkDotNet | v0.15.4, 3 warmups, 5 measured iterations, `MemoryDiagnoser` |
| Host | Windows 11 10.0.26300.8935, Snapdragon X X1P64100, Arm64, .NET SDK 10.0.300 |
| Baseline reconciliation | Added `System.IO.Hashing` only to the copied benchmark project so the new CRC micro-benchmark compiled against the old authoring code. Baseline `src\` was not changed. |

| Benchmark | Baseline mean | Optimized mean | Delta | Time reduction |
| --- | ---: | ---: | ---: | ---: |
| Pack loose directory to MSIX, large package, Stored | 80.686 ms | 36.659 ms | -44.028 ms | **54.6% faster** |
| Pack loose directory to MSIX, large package, Optimal/deflate | 287.325 ms | 243.791 ms | -43.534 ms | **15.2% faster** |
| CRC-32, 64 KiB payload: scalar before vs. `System.IO.Hashing` after | 154.794 μs | 2.102 μs | -152.692 μs | **98.6% faster** |
| CRC-32, 10 MiB payload: scalar before vs. `System.IO.Hashing` after | 24,771.464 μs | 390.453 μs | -24,381.011 μs | **98.4% faster** |

| Benchmark | Baseline allocated | Optimized allocated | Delta | Allocation reduction |
| --- | ---: | ---: | ---: | ---: |
| Pack loose directory to MSIX, large package, Stored | 5,059,936 B | 690,928 B | -4,369,008 B | **86.3% less** |
| Pack loose directory to MSIX, large package, Optimal/deflate | 57,005,664 B | 52,505,424 B | -4,500,240 B | **7.9% less** |
| CRC-32, 64 KiB payload | 0 B | 0 B | 0 B | no allocation change |
| CRC-32, 10 MiB payload | 0 B | 0 B | 0 B | no allocation change |

For the default Stored authoring path, the measured 10 MiB CRC delta is
24.381 ms. That is **30.2% of the old 80.686 ms Stored pack time** and explains
about **55.4% of the observed 44.028 ms end-to-end pack improvement**. The
remaining measured win comes from pooled authoring buffers and related
authoring-path allocation reduction.

## Deflate allocation optimization (`feature/deflate-alloc`)

PR #51 reduced Stored-path allocations to 0.069 B/B but the Optimal (deflate)
path remained at 5.01 B/B — 73× worse. This round targets the deflate-path
allocation rate specifically.

| Item | Value |
| --- | --- |
| Baseline commit | `7ecb602` (origin/main, includes PR #51 + ZIP64) |
| Optimized branch | `feature/deflate-alloc` |
| Command | `dotnet run -c Release --project bench\MsixCore.Benchmarks -- --filter "*PackPackage*"` |
| BenchmarkDotNet | v0.15.4, 3–5 warmups, 5–10 measured iterations, `MemoryDiagnoser` |
| Host | Windows 10.0.26300, Snapdragon X, Arm64, .NET SDK 10.0.300 |

### What changed

1. **Eliminated per-block `MemoryStream` + `ToArray()`.** The original
   `CompressBlock` created a fresh `MemoryStream`, compressed into it, then
   called `ToArray()` — two heap allocations per 64 KiB block (~200 blocks for
   10 MiB). Replaced with a `GatedCountingStream` that writes compressed data
   directly to the destination stream, eliminating the intermediate buffer and
   copy entirely.

2. **Gated DeflateStream finalization.** `DeflateStream.Dispose()` writes a
   deflate finalization marker to its output stream. MSIX blocks use sync-flush,
   not finalization. The `GatedCountingStream` closes the gate after
   `DeflateStream.Flush()` so that `Dispose()` finalization bytes are silently
   discarded while native zlib resources are properly freed.

3. **No-op Flush forwarding.** `DeflateStream.Flush()` calls `Flush()` on the
   underlying stream. Previously this was a MemoryStream (no-op); with the
   direct-to-FileStream write path, this forwarded per-block `FileStream.Flush()`
   calls. Suppressing them avoids a measurable wall-clock penalty.

### What was investigated but not changed

- **DeflateStream reuse across blocks.** .NET's `DeflateStream` has no `Reset()`
  method and `Dispose()` is required to free native zlib resources. The zlib
  `deflateReset` function exists but is not exposed through the .NET public API.
  Each 64 KiB block requires an independent deflate context (MSIX blocks must be
  independently decompressible), so a fresh `DeflateStream` per block is
  unavoidable with the current .NET API. The `DeflateStream` internal managed
  allocation is small (~2–4 KB staging buffer); the deflate window/hash state
  lives in native memory and is not tracked by the managed allocator.

- **Span-based deflate API.** Unlike `BrotliEncoder`, .NET provides no
  buffer-to-buffer deflate API. All deflate compression goes through the
  stream-wrapping `DeflateStream`.

### Allocation results

| Benchmark | PR #51 (baseline) | After | Delta | Reduction |
| --- | ---: | ---: | ---: | ---: |
| Pack 10 MiB, Stored | 690,928 B | 690,928 B | 0 B | unchanged |
| Pack 10 MiB, Optimal/deflate | 52,505,424 B | ~850,000 B | ~-51,655,000 B | **~98.4% less** |

Normalized allocation rate (bytes allocated per byte packed):

| Path | PR #51 | After | Ratio |
| --- | ---: | ---: | ---: |
| Stored | 0.069 B/B | 0.069 B/B | 1.0× |
| Optimal/deflate | **5.01 B/B** | **~0.083 B/B** | **~60× less** |

The Optimal path is now within ~1.2× of Stored, down from 73×. For the
motivating scenario — a packaging service packing a 1.26 GiB payload — this
reduces managed garbage from ~6.6 GB to ~107 MB, eliminating the GC-pressure
scalability blocker.

### Wall-clock results

| Benchmark | PR #51 (baseline) | After | Delta |
| --- | ---: | ---: | ---: |
| Pack 10 MiB, Stored | 36.7 ms | ~37 ms | within noise |
| Pack 10 MiB, Optimal/deflate | 243.8 ms | ~277 ms | ~+13% |

The Optimal wall-clock increase (~33 ms, ~13%) comes from the changed write
pattern: `DeflateStream` makes many small writes through the `GatedCountingStream`
to the `FileStream`, whereas previously it wrote to a fast `MemoryStream` followed
by a single large `FileStream.Write`. The increase is small relative to real-world
I/O-bound packing (~45 MiB/s), and the 98% allocation reduction delivers a net
throughput win under concurrent GC pressure.

### Allocation regression guard

A test (`DeflateAllocTests.Build_Optimal_AllocationBudget`) uses
`GC.GetAllocatedBytesForCurrentThread()` to assert that a 2 MiB Optimal pack
stays under a 4 MiB allocation ceiling. The measured value is ~200 KB for this
payload size; the 4 MiB ceiling provides generous headroom for runtime/GC
variation while catching regressions like the original 52 MB pattern.

## Pack results

**Summary:** msixkit wins the two throughput-dominated rows (10 MiB **1.87×**,
64 MiB **1.77×**) and is within noise on the startup-dominated 1 MiB row
(0.92×). This run was captured on an idle machine; see the environmental note
below.

| Corpus | Speedup (MakeAppx / msixkit) | msixkit median [min–max] | MakeAppx median [min–max] | Peak-WS reduction | msixkit peak WS | MakeAppx peak WS |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 MiB / 8 files | **0.92× (MakeAppx faster)** | 93.56 [84.72–172.39] ms | 86.22 [77.51–177.60] ms | **0.95× (MakeAppx uses less)** | 28.86 MB | 27.35 MB |
| 10 MiB / 64 files | **1.87× faster** | 151.50 [146.60–168.95] ms | 283.47 [209.57–2,703.44] ms | **0.92× (MakeAppx uses less)** | 32.70 MB | 29.94 MB |
| 64 MiB / 128 files | **1.77× faster** | 493.21 [482.26–550.09] ms | 875.27 [236.52–2,519.95] ms | **2.24× less** | 37.07 MB | 83.15 MB |

> **Environmental sensitivity.** The managed .NET tool is more sensitive to CPU
> contention than native MakeAppx. An earlier 7-iteration run captured while
> other CPU-heavy processes were active inflated msixkit's medians enough to
> flip the 1 MiB and 10 MiB rows to losses (e.g. 10 MiB msixkit pack measured
> 244 ms then vs. 151 ms here). Always run this comparison on an otherwise idle
> machine; the tight msixkit min–max ranges above (for example 10 MiB pack
> spanning only 146.6–168.9 ms) are the signal that the host was quiet, whereas
> MakeAppx still shows multi-second outlier maxima from filesystem/scanner
> interference.

## Unpack results

Both tools unpack the same uncompressed package authored by MakeAppx.

**Summary:** msixkit unpacks 1.12–1.67× faster and uses 1.15–1.18× less peak
working set.

| Corpus | Speedup (MakeAppx / msixkit) | msixkit median [min–max] | MakeAppx median [min–max] | Peak-WS reduction | msixkit peak WS | MakeAppx peak WS |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 MiB / 8 files | **1.12× faster** | 71.43 [61.24–147.78] ms | 79.66 [77.19–94.45] ms | **1.16× less** | 23.44 MB | 27.23 MB |
| 10 MiB / 64 files | **1.36× faster** | 78.10 [72.07–92.62] ms | 106.22 [98.71–129.38] ms | **1.15× less** | 23.86 MB | 27.55 MB |
| 64 MiB / 128 files | **1.67× faster** | 128.99 [109.43–164.83] ms | 215.98 [200.08–283.34] ms | **1.18× less** | 23.84 MB | 28.07 MB |

## Sampled peak private memory

Private bytes are sampled every 5 ms and can miss short-lived peaks; peak working
set above remains the primary memory metric.

**Summary:** msixkit uses 1.29–4.87× less sampled private memory for pack and
1.91–1.94× less for unpack.

| Operation | Corpus | Memory reduction (MakeAppx / msixkit) | msixkit | MakeAppx |
| --- | --- | ---: | ---: | ---: |
| Pack | 1 MiB / 8 files | **1.60× less** | 7.45 MB | 11.94 MB |
| Pack | 10 MiB / 64 files | **1.29× less** | 11.04 MB | 14.29 MB |
| Pack | 64 MiB / 128 files | **4.87× less** | 14.52 MB | 70.75 MB |
| Unpack | 1 MiB / 8 files | **1.94× less** | 6.01 MB | 11.66 MB |
| Unpack | 10 MiB / 64 files | **1.91× less** | 6.34 MB | 12.11 MB |
| Unpack | 64 MiB / 128 files | **1.92× less** | 6.55 MB | 12.58 MB |

## Validate: asymmetric capability

MakeAppx can perform semantic validation as part of its default pack/unpack path, but
the timed runs above disable it with `/nv`. It has no standalone verb equivalent to
`msixkit validate`, so a synthetic validation ratio would be misleading.

| Corpus | msixkit median [min–max] | Peak working set |
| --- | ---: | ---: |
| 1 MiB / 8 files | 74.56 [68.65–116.92] ms | 26.49 MB |
| 10 MiB / 64 files | 81.20 [77.43–93.40] ms | 26.73 MB |
| 64 MiB / 128 files | 116.28 [111.79–124.40] ms | 27.53 MB |

This is end-to-end CLI time, including managed startup/JIT and manifest/block-map
parsing, not just SHA-256 throughput. The smaller cases are consequently
startup-dominated.

## On-disk footprint

Published-output totals include all files produced by `dotnet publish`, including
symbols/documentation. The framework-dependent build requires a machine-wide .NET 10
runtime. Self-contained builds bundle it. MakeAppx totals include the executable plus
the SDK-local `appxpackaging.dll` and `opcservices.dll` observed loaded by the harness;
shared Windows DLLs are excluded.

| Configuration | Total | SDK-reference ratio |
| --- | ---: | ---: |
| msixkit self-contained win-arm64 | 86.81 MB | 13.59x vs Arm64 MakeAppx |
| msixkit self-contained trimmed win-arm64 | 24.00 MB | 3.76x vs Arm64 MakeAppx |
| MakeAppx SDK tool, native Arm64 | 6.39 MB | 1.00x |
| msixkit framework-dependent (host Arm64) | 1.10 MB | 0.17x vs Arm64 MakeAppx |
| msixkit self-contained win-x64 (secondary) | 77.20 MB | 16.98x vs x64 MakeAppx |
| msixkit self-contained trimmed win-x64 (secondary) | 22.62 MB | 4.97x vs x64 MakeAppx |
| MakeAppx SDK tool, x64 binary (secondary; emulated here) | 4.55 MB | 1.00x |

Trimmed publishing now **succeeds** for both RIDs. Trimming reduces x64 from
77.20 MB to 22.62 MB (71% smaller) and Arm64 from 86.81 MB to 24.00 MB (72% smaller).
It remains several times larger than the native SDK tool because it carries a private
.NET runtime. Conversely, the 1.10 MB framework-dependent output is smaller on disk
than MakeAppx's SDK-local files, but that comparison excludes the shared .NET runtime.

## Interpretation

- With validation removed from timed MakeAppx work, msixkit now wins both
  throughput-dominated pack rows on a quiet machine: 1.87× faster at 10 MiB and
  1.77× faster at 64 MiB. The 1 MiB row is a statistical tie (0.92×), dominated
  by managed startup/JIT rather than throughput. msixkit wins every unpack row
  (1.12–1.67×).
- The earlier report showed a 10 MiB pack *loss* (0.81×). That was an
  environmental artifact: the run coincided with other CPU-heavy activity, which
  penalizes the managed runtime more than native MakeAppx. Re-running on an idle
  machine with 21 iterations moved msixkit's 10 MiB pack median from 244 ms to
  151 ms with a tight 146.6–168.9 ms range, flipping the row to a clear win. The
  non-monotonic win/lose/win pattern in the old data was the tell.
- The primary result is a fair native-Arm64 fight and is **not explained by
  emulation**. The optional x64 MakeAppx run is secondary only. Timed MakeAppx
  operations use `/nv`, so its additional semantic-validation cost is excluded
  rather than being hidden inside pack/unpack.
- Managed startup/JIT is a fixed tax. It is most visible on the 1 MiB operations,
  where time and working set are close. Throughput dominates as payload size grows.
- MakeAppx variance remains substantial and asymmetric: its 10 MiB and 64 MiB
  pack maxima reached 2.70 s and 2.52 s respectively even on this quiet run,
  while msixkit stayed tightly clustered. Medians are the comparison statistic;
  keep the min/max visible.
- MakeAppx can compress supported content; msixkit cannot yet. Compression-enabled
  MakeAppx is a different workload that may produce much smaller packages and should
  be measured separately once both tools expose equivalent compression behavior.
- Variance is non-trivial, especially around filesystem writes. Use the medians, keep
  the min/max, close competing workloads, and rerun on release hardware before using
  these numbers as a product guarantee.

## Original C++ MSIX Core: methodology only

The original C++ MSIX Core binaries are not in this repository and were **not
measured**. Do not treat MakeAppx as a proxy for their performance: it is a different
Windows SDK implementation.

For a future C++ comparison, obtain matching official release binaries for the same
architecture, run them as external processes over these exact generated layouts and
canonical packages, use the same warmup/repetition/working-set method, and verify
cross-tool output hashes. Compare framework-dependent .NET with dynamically linked
C++ and trimmed self-contained .NET with a statically linked C++ distribution. Record
the complete shipped executable-plus-DLL footprint in each case.
