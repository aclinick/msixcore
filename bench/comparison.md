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

- each msixmgr-authored package is unpacked by MakeAppx, then every source file is
  checked by SHA-256;
- each MakeAppx-authored package is opened by `msixmgr inspect`;
- timed unpack results from both tools are checked against the source layout.

Headline multipliers below are **MakeAppx / msixmgr**. **Greater than 1.00× means
msixmgr is faster or uses less memory**; below 1.00× would honestly show a MakeAppx
win. Min/max are included because filesystem and security-scanner interference is
visible on this workstation; medians are the comparison statistic.

## Pack results

**Summary:** the speedup multiplier spans 0.81–2.55×: msixmgr wins the 1 MiB
and 64 MiB rows, while MakeAppx wins the 10 MiB row; memory winners also vary.

| Corpus | Speedup (MakeAppx / msixmgr) | msixmgr median [min–max] | MakeAppx median [min–max] | Peak-WS reduction | msixmgr peak WS | MakeAppx peak WS |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 MiB / 8 files | **1.32× faster** | 106.06 [95.95–150.49] ms | 140.34 [86.43–159.34] ms | **0.90× (MakeAppx uses less)** | 30.30 MB | 27.40 MB |
| 10 MiB / 64 files | **0.81× (MakeAppx faster)** | 244.45 [207.86–253.35] ms | 197.43 [135.51–215.29] ms | **0.89× (MakeAppx uses less)** | 33.59 MB | 30.02 MB |
| 64 MiB / 128 files | **2.55× faster** | 680.58 [571.43–765.28] ms | 1,735.00 [350.38–3,407.32] ms | **2.24× less** | 37.24 MB | 83.25 MB |

## Unpack results

Both tools unpack the same uncompressed package authored by MakeAppx.

**Summary:** msixmgr unpacks 1.07–1.46× faster and uses 1.12–1.20× less peak
working set.

| Corpus | Speedup (MakeAppx / msixmgr) | msixmgr median [min–max] | MakeAppx median [min–max] | Peak-WS reduction | msixmgr peak WS | MakeAppx peak WS |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 MiB / 8 files | **1.10× faster** | 133.49 [82.76–166.57] ms | 146.83 [100.80–184.29] ms | **1.20× less** | 22.70 MB | 27.29 MB |
| 10 MiB / 64 files | **1.07× faster** | 146.43 [96.60–178.03] ms | 156.09 [123.48–199.30] ms | **1.12× less** | 24.66 MB | 27.61 MB |
| 64 MiB / 128 files | **1.46× faster** | 206.52 [133.59–260.02] ms | 301.48 [228.22–368.36] ms | **1.18× less** | 23.80 MB | 28.10 MB |

## Sampled peak private memory

Private bytes are sampled every 5 ms and can miss short-lived peaks; peak working
set above remains the primary memory metric.

**Summary:** msixmgr uses 1.31–4.84× less sampled private memory for pack and
1.78–1.92× less for unpack.

| Operation | Corpus | Memory reduction (MakeAppx / msixmgr) | msixmgr | MakeAppx |
| --- | --- | ---: | ---: | ---: |
| Pack | 1 MiB / 8 files | **1.50× less** | 7.99 MB | 12.00 MB |
| Pack | 10 MiB / 64 files | **1.31× less** | 11.09 MB | 14.50 MB |
| Pack | 64 MiB / 128 files | **4.84× less** | 14.64 MB | 70.79 MB |
| Unpack | 1 MiB / 8 files | **1.78× less** | 5.77 MB | 10.27 MB |
| Unpack | 10 MiB / 64 files | **1.92× less** | 6.30 MB | 12.09 MB |
| Unpack | 64 MiB / 128 files | **1.92× less** | 6.55 MB | 12.57 MB |

## Validate: asymmetric capability

MakeAppx can perform semantic validation as part of its default pack/unpack path, but
the timed runs above disable it with `/nv`. It has no standalone verb equivalent to
`msixmgr validate`, so a synthetic validation ratio would be misleading.

| Corpus | msixmgr median [min–max] | Peak working set |
| --- | ---: | ---: |
| 1 MiB / 8 files | 135.69 [88.96–162.86] ms | 27.20 MB |
| 10 MiB / 64 files | 173.17 [97.31–181.56] ms | 31.53 MB |
| 64 MiB / 128 files | 183.73 [134.38–193.26] ms | 33.71 MB |

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

- With validation removed from timed MakeAppx work, the pack result is mixed:
  msixmgr is 1.32× faster at 1 MiB and 2.55× faster at 64 MiB, while MakeAppx
  is about 1.24× faster at 10 MiB (the displayed msixmgr speedup is 0.81×).
  msixmgr wins all unpack rows, but only by 1.07–1.46×.
- The primary result is a fair native-Arm64 fight and is **not explained by
  emulation**. The optional x64 MakeAppx run is secondary only. Timed MakeAppx
  operations use `/nv`, so its additional semantic-validation cost is excluded
  rather than being hidden inside pack/unpack.
- Managed startup/JIT is a fixed tax. It is most visible on the 1 MiB operations,
  where time and working set are close. Throughput dominates as payload size grows.
- Large-pack variance remains substantial: native MakeAppx ranged from 350 ms to
  3.41 s for the 64 MiB corpus. The median is reported, but this row should be
  rerun on a quieter release machine before treating 2.55× as a stable guarantee.
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
