# SDK tool comparison — generated results

Generated: 2026-07-25T10:12:20.0533574-07:00

- Host: `Microsoft Windows 10.0.26300` (Arm64)
- .NET SDK: `10.0.300`
- MakeAppx: `C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\arm64\makeappx.exe` (10.0.26100.8249 (WinBuild.160101.0800))
- Repetitions: 21 measured after one discarded warmup
- Packages are unsigned and stored/uncompressed (MakeAppx `/nc`), matching msixkit's current authoring mode.
- Timed MakeAppx pack/unpack runs use `/nv` to exclude its extra semantic validation; untimed correctness checks keep validation enabled.
- Headline multipliers are **MakeAppx / msixkit**; **greater than 1.00× means msixkit is faster or uses less memory**.

## Pack

**Summary:** speedup is 0.92–1.87× and peak-working-set reduction is 0.92–2.24×; values below 1× favor MakeAppx.

| Corpus | Speedup (MakeAppx / msixkit) | msixkit time median [min–max] ms | MakeAppx time median [min–max] ms | Peak-WS reduction | msixkit peak WS | MakeAppx peak WS |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| small-1MiB-8files (1.00 MB) | **0.92× (MakeAppx faster)** | 93.56 [84.72–172.39] | 86.22 [77.51–177.60] | **0.95× (MakeAppx uses less)** | 28.86 MB | 27.35 MB |
| medium-10MiB-64files (10.00 MB) | **1.87× faster** | 151.50 [146.60–168.95] | 283.47 [209.57–2,703.44] | **0.92× (MakeAppx uses less)** | 32.70 MB | 29.94 MB |
| large-64MiB-128files (64.00 MB) | **1.77× faster** | 493.21 [482.26–550.09] | 875.27 [236.52–2,519.95] | **2.24× less** | 37.07 MB | 83.15 MB |

## Unpack

**Summary:** msixkit is 1.12–1.67× faster and uses 1.15–1.18× less peak working set.

| Corpus | Speedup (MakeAppx / msixkit) | msixkit time median [min–max] ms | MakeAppx time median [min–max] ms | Peak-WS reduction | msixkit peak WS | MakeAppx peak WS |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| small-1MiB-8files (1.00 MB) | **1.12× faster** | 71.43 [61.24–147.78] | 79.66 [77.19–94.45] | **1.16× less** | 23.44 MB | 27.23 MB |
| medium-10MiB-64files (10.00 MB) | **1.36× faster** | 78.10 [72.07–92.62] | 106.22 [98.71–129.38] | **1.15× less** | 23.86 MB | 27.55 MB |
| large-64MiB-128files (64.00 MB) | **1.67× faster** | 128.99 [109.43–164.83] | 215.98 [200.08–283.34] | **1.18× less** | 23.84 MB | 28.07 MB |

## Validate (no MakeAppx equivalent)

MakeAppx has no standalone block-map verification verb, so these msixkit results are reported without a ratio rather than forcing a misleading comparison.

| Corpus | msixkit time median [min–max] ms | Peak working set |
| --- | ---: | ---: |
| small-1MiB-8files (1.00 MB) | 74.56 [68.65–116.92] | 26.49 MB |
| medium-10MiB-64files (10.00 MB) | 81.20 [77.43–93.40] | 26.73 MB |
| large-64MiB-128files (64.00 MB) | 116.28 [111.79–124.40] | 27.53 MB |

## Sampled peak private bytes

Private bytes are sampled every 5 ms, so short-lived peaks can be missed; peak working set above uses the OS-reported process peak sampled while the process is alive.

**Summary:** msixkit uses 1.29–4.87× less sampled private memory for pack and 1.91–1.94× less for unpack.

| Operation | Corpus | Memory reduction (MakeAppx / msixkit) | msixkit | MakeAppx |
| --- | --- | ---: | ---: | ---: |
| Pack | small-1MiB-8files | **1.60× less** | 7.45 MB | 11.94 MB |
| Pack | medium-10MiB-64files | **1.29× less** | 11.04 MB | 14.29 MB |
| Pack | large-64MiB-128files | **4.87× less** | 14.52 MB | 70.75 MB |
| Unpack | small-1MiB-8files | **1.94× less** | 6.01 MB | 11.66 MB |
| Unpack | medium-10MiB-64files | **1.91× less** | 6.34 MB | 12.11 MB |
| Unpack | large-64MiB-128files | **1.92× less** | 6.55 MB | 12.58 MB |

## Observed MakeAppx SDK-local footprint

| Total | Files |
| ---: | --- |
| 6.39 MB | AppxPackaging.dll, makeappx.exe, OpcServices.DLL |

Cross-tool checks passed: every msixkit package unpacked with MakeAppx and matched its source files; every MakeAppx package opened with `msixkit inspect`; both tools reproduced every source file from the canonical packages.
