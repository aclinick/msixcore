# SDK tool comparison — generated results

Generated: 2026-07-25T08:08:51.1203118-07:00

- Host: `Microsoft Windows 10.0.26300` (Arm64)
- .NET SDK: `10.0.300`
- MakeAppx: `C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\arm64\makeappx.exe` (10.0.26100.8249 (WinBuild.160101.0800))
- Repetitions: 7 measured after one discarded warmup
- Packages are unsigned and stored/uncompressed (MakeAppx `/nc`), matching msixmgr's current authoring mode.
- Ratio is **msixmgr / MakeAppx**; below 1.00 means msixmgr used less time or memory.

## Pack

| Corpus | msixmgr time median [min–max] ms | MakeAppx time median [min–max] ms | Time ratio | msixmgr peak WS | MakeAppx peak WS | WS ratio |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| small-1MiB-8files (1.00 MB) | 177.68 [122.65–220.24] | 253.41 [174.87–405.58] | 0.70x | 30.34 MB | 30.76 MB | 0.99x |
| medium-10MiB-64files (10.00 MB) | 307.59 [185.93–656.97] | 738.49 [518.32–787.27] | 0.42x | 33.59 MB | 37.52 MB | 0.90x |
| large-64MiB-128files (64.00 MB) | 765.14 [635.45–887.02] | 1,427.76 [861.62–1,490.26] | 0.54x | 39.14 MB | 94.45 MB | 0.41x |

## Unpack

| Corpus | msixmgr time median [min–max] ms | MakeAppx time median [min–max] ms | Time ratio | msixmgr peak WS | MakeAppx peak WS | WS ratio |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| small-1MiB-8files (1.00 MB) | 130.27 [74.13–150.71] | 220.29 [139.39–326.57] | 0.59x | 23.67 MB | 30.55 MB | 0.77x |
| medium-10MiB-64files (10.00 MB) | 208.03 [146.96–257.20] | 383.76 [203.81–424.59] | 0.54x | 23.79 MB | 30.83 MB | 0.77x |
| large-64MiB-128files (64.00 MB) | 298.05 [220.69–329.23] | 561.69 [431.45–734.55] | 0.53x | 23.79 MB | 31.25 MB | 0.76x |

## Validate (no MakeAppx equivalent)

MakeAppx has no standalone block-map verification verb, so these msixmgr results are reported without a ratio rather than forcing a misleading comparison.

| Corpus | msixmgr time median [min–max] ms | Peak working set |
| --- | ---: | ---: |
| small-1MiB-8files (1.00 MB) | 203.84 [118.16–223.87] | 28.63 MB |
| medium-10MiB-64files (10.00 MB) | 284.12 [184.27–347.40] | 32.38 MB |
| large-64MiB-128files (64.00 MB) | 321.72 [185.71–495.24] | 33.73 MB |

## Sampled peak private bytes

Private bytes are sampled every 5 ms, so short-lived peaks can be missed; peak working set above uses the OS-reported process peak sampled while the process is alive.

| Operation | Corpus | msixmgr | MakeAppx | Ratio |
| --- | --- | ---: | ---: | ---: |
| Pack | small-1MiB-8files | 8.01 MB | 15.21 MB | 0.53x |
| Pack | medium-10MiB-64files | 11.19 MB | 22.00 MB | 0.51x |
| Pack | large-64MiB-128files | 15.50 MB | 79.36 MB | 0.20x |
| Unpack | small-1MiB-8files | 6.20 MB | 14.78 MB | 0.42x |
| Unpack | medium-10MiB-64files | 6.40 MB | 15.27 MB | 0.42x |
| Unpack | large-64MiB-128files | 6.54 MB | 15.61 MB | 0.42x |

## Observed MakeAppx SDK-local footprint

| Total | Files |
| ---: | --- |
| 6.39 MB | AppxPackaging.dll, makeappx.exe, OpcServices.DLL |

Cross-tool checks passed: every msixmgr package unpacked with MakeAppx and matched its source files; every MakeAppx package opened with `msixmgr inspect`; both tools reproduced every source file from the canonical packages.
