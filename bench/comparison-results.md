# SDK tool comparison — generated results

Generated: 2026-07-25T08:32:49.0632368-07:00

- Host: `Microsoft Windows 10.0.26300` (Arm64)
- .NET SDK: `10.0.300`
- MakeAppx: `C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\arm64\makeappx.exe` (10.0.26100.8249 (WinBuild.160101.0800))
- Repetitions: 7 measured after one discarded warmup
- Packages are unsigned and stored/uncompressed (MakeAppx `/nc`), matching msixmgr's current authoring mode.
- Timed MakeAppx pack/unpack runs use `/nv` to exclude its extra semantic validation; untimed correctness checks keep validation enabled.
- Headline multipliers are **MakeAppx / msixmgr**; **greater than 1.00× means msixmgr is faster or uses less memory**.

## Pack

**Summary:** speedup is 0.81–2.55× and peak-working-set reduction is 0.89–2.24×; values below 1× favor MakeAppx.

| Corpus | Speedup (MakeAppx / msixmgr) | msixmgr time median [min–max] ms | MakeAppx time median [min–max] ms | Peak-WS reduction | msixmgr peak WS | MakeAppx peak WS |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| small-1MiB-8files (1.00 MB) | **1.32× faster** | 106.06 [95.95–150.49] | 140.34 [86.43–159.34] | **0.90× (MakeAppx uses less)** | 30.30 MB | 27.40 MB |
| medium-10MiB-64files (10.00 MB) | **0.81× (MakeAppx faster)** | 244.45 [207.86–253.35] | 197.43 [135.51–215.29] | **0.89× (MakeAppx uses less)** | 33.59 MB | 30.02 MB |
| large-64MiB-128files (64.00 MB) | **2.55× faster** | 680.58 [571.43–765.28] | 1,735.00 [350.38–3,407.32] | **2.24× less** | 37.24 MB | 83.25 MB |

## Unpack

**Summary:** msixmgr is 1.07–1.46× faster and uses 1.12–1.20× less peak working set.

| Corpus | Speedup (MakeAppx / msixmgr) | msixmgr time median [min–max] ms | MakeAppx time median [min–max] ms | Peak-WS reduction | msixmgr peak WS | MakeAppx peak WS |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| small-1MiB-8files (1.00 MB) | **1.10× faster** | 133.49 [82.76–166.57] | 146.83 [100.80–184.29] | **1.20× less** | 22.70 MB | 27.29 MB |
| medium-10MiB-64files (10.00 MB) | **1.07× faster** | 146.43 [96.60–178.03] | 156.09 [123.48–199.30] | **1.12× less** | 24.66 MB | 27.61 MB |
| large-64MiB-128files (64.00 MB) | **1.46× faster** | 206.52 [133.59–260.02] | 301.48 [228.22–368.36] | **1.18× less** | 23.80 MB | 28.10 MB |

## Validate (no MakeAppx equivalent)

MakeAppx has no standalone block-map verification verb, so these msixmgr results are reported without a ratio rather than forcing a misleading comparison.

| Corpus | msixmgr time median [min–max] ms | Peak working set |
| --- | ---: | ---: |
| small-1MiB-8files (1.00 MB) | 135.69 [88.96–162.86] | 27.20 MB |
| medium-10MiB-64files (10.00 MB) | 173.17 [97.31–181.56] | 31.53 MB |
| large-64MiB-128files (64.00 MB) | 183.73 [134.38–193.26] | 33.71 MB |

## Sampled peak private bytes

Private bytes are sampled every 5 ms, so short-lived peaks can be missed; peak working set above uses the OS-reported process peak sampled while the process is alive.

**Summary:** msixmgr uses 1.31–4.84× less sampled private memory for pack and 1.78–1.92× less for unpack.

| Operation | Corpus | Memory reduction (MakeAppx / msixmgr) | msixmgr | MakeAppx |
| --- | --- | ---: | ---: | ---: |
| Pack | small-1MiB-8files | **1.50× less** | 7.99 MB | 12.00 MB |
| Pack | medium-10MiB-64files | **1.31× less** | 11.09 MB | 14.50 MB |
| Pack | large-64MiB-128files | **4.84× less** | 14.64 MB | 70.79 MB |
| Unpack | small-1MiB-8files | **1.78× less** | 5.77 MB | 10.27 MB |
| Unpack | medium-10MiB-64files | **1.92× less** | 6.30 MB | 12.09 MB |
| Unpack | large-64MiB-128files | **1.92× less** | 6.55 MB | 12.57 MB |

## Observed MakeAppx SDK-local footprint

| Total | Files |
| ---: | --- |
| 6.39 MB | AppxPackaging.dll, makeappx.exe, OpcServices.DLL |

Cross-tool checks passed: every msixmgr package unpacked with MakeAppx and matched its source files; every MakeAppx package opened with `msixmgr inspect`; both tools reproduced every source file from the canonical packages.
