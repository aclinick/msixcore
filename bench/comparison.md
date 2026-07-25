# msixmgr vs. Windows SDK MakeAppx

This is a measured, reproducible head-to-head comparison of the .NET 10 `msixmgr`
port and Windows SDK `makeappx.exe`. Raw run summaries are in
[`comparison-results.md`](comparison-results.md); per-run data is regenerated as
`comparison-results.json` by the harness.

> **Primary comparison: native Arm64 vs. native Arm64.** The .NET runtime,
> `msixmgr`, and Windows SDK MakeAppx are all native Arm64 binaries on this
> Snapdragon X host. No emulation is involved in the headline runtime results.
> The SDK's x64 MakeAppx can be selected explicitly as a secondary emulation
> curiosity, but those numbers must be kept separate.

## Environment

| Item | Value |
| --- | --- |
| Host | Windows 10.0.26300, Snapdragon X, Arm64 |
| .NET | SDK 10.0.300; .NET 10 Arm64 runtime |
| msixmgr | Release `net10.0` apphost, native Arm64 |
| MakeAppx | Windows SDK 10.0.26100.8249, native Arm64 |
| Repetitions | 1 discarded warmup + 7 measured processes |
| Package mode | Unsigned, uncompressed/stored (`makeappx /nc`) |

## Methodology

Run from the repository root:

```powershell
pwsh bench\Compare-Tools.ps1 -Iterations 7
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

`Compare-Tools.ps1` builds `src\msixmgr` in Release and deterministically generates
three valid loose package layouts: 1 MiB/8 payload files, 10 MiB/64 files, and
64 MiB/128 files. The same layouts and canonical packages are used by both tools.
Compression is disabled because this port currently writes stored entries; otherwise
MakeAppx compression would change both the work performed and unpack input size.

Every sample starts a fresh external process. Wall time uses
`System.Diagnostics.Stopwatch` from process start through exit. Peak working set uses
the process object's OS-reported peak while alive. “Private bytes” is a 5 ms sampled
maximum and can miss very short peaks, so working set is the primary memory result.
Output deletion and corpus generation are outside the timed region. Disk data is
warmed by one discarded operation per tool/corpus.

Correctness is part of the harness:

- each msixmgr-authored package is unpacked by MakeAppx, then every source file is
  checked by SHA-256;
- each MakeAppx-authored package is opened by `msixmgr inspect`;
- timed unpack results from both tools are checked against the source layout.

Ratios below are **msixmgr / MakeAppx**. Below 1.00 means msixmgr used less time or
memory. Min/max are included because filesystem and security-scanner interference is
visible on this workstation; medians are the comparison statistic.

## Pack results

| Corpus | msixmgr median [min–max] | MakeAppx median [min–max] | Time ratio | msixmgr peak WS | MakeAppx peak WS | WS ratio |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 MiB / 8 files | 177.68 [122.65–220.24] ms | 253.41 [174.87–405.58] ms | 0.70x | 30.34 MB | 30.76 MB | 0.99x |
| 10 MiB / 64 files | 307.59 [185.93–656.97] ms | 738.49 [518.32–787.27] ms | 0.42x | 33.59 MB | 37.52 MB | 0.90x |
| 64 MiB / 128 files | 765.14 [635.45–887.02] ms | 1,427.76 [861.62–1,490.26] ms | 0.54x | 39.14 MB | 94.45 MB | 0.41x |

## Unpack results

Both tools unpack the same uncompressed package authored by MakeAppx.

| Corpus | msixmgr median [min–max] | MakeAppx median [min–max] | Time ratio | msixmgr peak WS | MakeAppx peak WS | WS ratio |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 MiB / 8 files | 130.27 [74.13–150.71] ms | 220.29 [139.39–326.57] ms | 0.59x | 23.67 MB | 30.55 MB | 0.77x |
| 10 MiB / 64 files | 208.03 [146.96–257.20] ms | 383.76 [203.81–424.59] ms | 0.54x | 23.79 MB | 30.83 MB | 0.77x |
| 64 MiB / 128 files | 298.05 [220.69–329.23] ms | 561.69 [431.45–734.55] ms | 0.53x | 23.79 MB | 31.25 MB | 0.76x |

## Validate: asymmetric capability

MakeAppx validates while packing/unpacking but has no standalone verb equivalent to
`msixmgr validate`. A synthetic MakeAppx ratio would therefore be misleading.

| Corpus | msixmgr median [min–max] | Peak working set |
| --- | ---: | ---: |
| 1 MiB / 8 files | 203.84 [118.16–223.87] ms | 28.63 MB |
| 10 MiB / 64 files | 284.12 [184.27–347.40] ms | 32.38 MB |
| 64 MiB / 128 files | 321.72 [185.71–495.24] ms | 33.73 MB |

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
| msixmgr self-contained win-arm64 | 86.81 MB | 13.59x vs Arm64 MakeAppx |
| msixmgr self-contained trimmed win-arm64 | 24.00 MB | 3.76x vs Arm64 MakeAppx |
| MakeAppx SDK tool, native Arm64 | 6.39 MB | 1.00x |
| msixmgr framework-dependent (host Arm64) | 1.10 MB | 0.17x vs Arm64 MakeAppx |
| msixmgr self-contained win-x64 (secondary) | 77.20 MB | 16.98x vs x64 MakeAppx |
| msixmgr self-contained trimmed win-x64 (secondary) | 22.62 MB | 4.97x vs x64 MakeAppx |
| MakeAppx SDK tool, x64 binary (secondary; emulated here) | 4.55 MB | 1.00x |

Trimmed publishing now **succeeds** for both RIDs. Trimming reduces x64 from
77.20 MB to 22.62 MB (71% smaller) and Arm64 from 86.81 MB to 24.00 MB (72% smaller).
It remains several times larger than the native SDK tool because it carries a private
.NET runtime. Conversely, the 1.10 MB framework-dependent output is smaller on disk
than MakeAppx's SDK-local files, but that comparison excludes the shared .NET runtime.

## Interpretation

- On this run, msixmgr won every measured runtime row: pack used 42–70% of MakeAppx
  time and unpack used 53–59%. This is a real result for these uncompressed corpora,
  but not evidence that managed code is universally faster.
- The primary result is a fair native-Arm64 fight and is **not explained by
  emulation**. The optional x64 MakeAppx run is secondary only.
  MakeAppx performs richer Windows package semantic work and its parallel pack path
  reached 94 MB peak working set on the 64 MiB corpus. msixmgr's narrower,
  stored-entry authoring path peaked at 39 MB.
- Managed startup/JIT is a fixed tax. It is most visible on the 1 MiB operations,
  where time and working set are close. Throughput dominates as payload size grows.
- MakeAppx can compress supported content; msixmgr cannot yet. Compression-enabled
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
