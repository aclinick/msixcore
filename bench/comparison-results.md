# SDK tool comparison — generated results

Generated: 2026-07-25T08:08:51.1203118-07:00

- Host: `Microsoft Windows 10.0.26300` (Arm64)
- .NET SDK: `10.0.300`
- MakeAppx: `C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\arm64\makeappx.exe` (10.0.26100.8249 (WinBuild.160101.0800))
- Repetitions: 7 measured after one discarded warmup
- Packages are unsigned and stored/uncompressed (MakeAppx `/nc`), matching msixmgr's current authoring mode.
- Headline multipliers are **MakeAppx / msixmgr**; **greater than 1.00× means msixmgr is faster or uses less memory**.

## Pack

**Summary:** msixmgr is 1.43–2.40× faster and uses 1.01–2.41× less peak working set.

| Corpus | Speedup (MakeAppx / msixmgr) | msixmgr time median [min–max] ms | MakeAppx time median [min–max] ms | Peak-WS reduction | msixmgr peak WS | MakeAppx peak WS |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| small-1MiB-8files (1.00 MB) | **1.43× faster** | 177.68 [122.65–220.24] | 253.41 [174.87–405.58] | **1.01× less** | 30.34 MB | 30.76 MB |
| medium-10MiB-64files (10.00 MB) | **2.40× faster** | 307.59 [185.93–656.97] | 738.49 [518.32–787.27] | **1.12× less** | 33.59 MB | 37.52 MB |
| large-64MiB-128files (64.00 MB) | **1.87× faster** | 765.14 [635.45–887.02] | 1,427.76 [861.62–1,490.26] | **2.41× less** | 39.14 MB | 94.45 MB |

## Unpack

**Summary:** msixmgr is 1.69–1.88× faster and uses 1.29–1.31× less peak working set.

| Corpus | Speedup (MakeAppx / msixmgr) | msixmgr time median [min–max] ms | MakeAppx time median [min–max] ms | Peak-WS reduction | msixmgr peak WS | MakeAppx peak WS |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| small-1MiB-8files (1.00 MB) | **1.69× faster** | 130.27 [74.13–150.71] | 220.29 [139.39–326.57] | **1.29× less** | 23.67 MB | 30.55 MB |
| medium-10MiB-64files (10.00 MB) | **1.84× faster** | 208.03 [146.96–257.20] | 383.76 [203.81–424.59] | **1.30× less** | 23.79 MB | 30.83 MB |
| large-64MiB-128files (64.00 MB) | **1.88× faster** | 298.05 [220.69–329.23] | 561.69 [431.45–734.55] | **1.31× less** | 23.79 MB | 31.25 MB |

## Validate (no MakeAppx equivalent)

MakeAppx has no standalone block-map verification verb, so these msixmgr results are reported without a ratio rather than forcing a misleading comparison.

| Corpus | msixmgr time median [min–max] ms | Peak working set |
| --- | ---: | ---: |
| small-1MiB-8files (1.00 MB) | 203.84 [118.16–223.87] | 28.63 MB |
| medium-10MiB-64files (10.00 MB) | 284.12 [184.27–347.40] | 32.38 MB |
| large-64MiB-128files (64.00 MB) | 321.72 [185.71–495.24] | 33.73 MB |

## Sampled peak private bytes

Private bytes are sampled every 5 ms, so short-lived peaks can be missed; peak working set above uses the OS-reported process peak sampled while the process is alive.

**Summary:** msixmgr uses 1.90–5.12× less sampled private memory for pack and about 2.39× less for unpack.

| Operation | Corpus | Memory reduction (MakeAppx / msixmgr) | msixmgr | MakeAppx |
| --- | --- | ---: | ---: | ---: |
| Pack | small-1MiB-8files | **1.90× less** | 8.01 MB | 15.21 MB |
| Pack | medium-10MiB-64files | **1.97× less** | 11.19 MB | 22.00 MB |
| Pack | large-64MiB-128files | **5.12× less** | 15.50 MB | 79.36 MB |
| Unpack | small-1MiB-8files | **2.38× less** | 6.20 MB | 14.78 MB |
| Unpack | medium-10MiB-64files | **2.39× less** | 6.40 MB | 15.27 MB |
| Unpack | large-64MiB-128files | **2.39× less** | 6.54 MB | 15.61 MB |

## Observed MakeAppx SDK-local footprint

| Total | Files |
| ---: | --- |
| 6.39 MB | AppxPackaging.dll, makeappx.exe, OpcServices.DLL |

Cross-tool checks passed: every msixmgr package unpacked with MakeAppx and matched its source files; every MakeAppx package opened with `msixmgr inspect`; both tools reproduced every source file from the canonical packages.
