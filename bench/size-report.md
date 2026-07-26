# msixkit published-size report

Generated: 2026-07-25 08:09:44 -07:00

- Runtime identifiers: `win-x64, win-arm64`
- Configuration: `Release`
- .NET SDK: `10.0.300`
- Host OS: `Microsoft Windows 10.0.26300`

## Totals

| Configuration | Total size | Files | msixkit host |
| --- | ---: | ---: | ---: |
| Framework-dependent (portable, host architecture) | 1.10 MB | 14 | 137.00 KB |
| Self-contained (win-x64) | 77.20 MB | 200 | 159.00 KB |
| Self-contained + trimmed (win-x64) | 22.62 MB | 68 | 159.00 KB |
| Self-contained (win-arm64) | 86.81 MB | 200 | 137.00 KB |
| Self-contained + trimmed (win-arm64) | 24.00 MB | 68 | 137.00 KB |

## Windows SDK MakeAppx footprint

The total includes `makeappx.exe` plus the SDK-local DLLs observed loaded by the
comparison harness (`appxpackaging.dll` and `opcservices.dll`). OS DLLs are excluded.

| SDK tool | Total size | Files |
| --- | ---: | --- |
| MakeAppx SDK tool (x64 binary; emulated on this Arm64 host) | 4.55 MB | makeappx.exe, appxpackaging.dll, opcservices.dll |
| MakeAppx SDK tool (Arm64 native) | 6.39 MB | makeappx.exe, appxpackaging.dll, opcservices.dll |

## Key assemblies (per configuration)

### Framework-dependent (portable, host architecture)

| Assembly | Size |
| --- | ---: |
| msixkit.exe | 137.00 KB |
| MsixCore.Packaging.dll | 89.00 KB |
| msixkit.dll | 75.00 KB |
| msixkit.pdb | 42.18 KB |
| MsixCore.PackageStore.dll | 24.00 KB |
| msixkit.xml | 7.32 KB |

### Self-contained (win-x64)

| Assembly | Size |
| --- | ---: |
| msixkit.exe | 159.00 KB |
| MsixCore.Packaging.dll | 89.00 KB |
| msixkit.dll | 74.50 KB |
| msixkit.pdb | 42.21 KB |
| msixkit.deps.json | 28.87 KB |
| MsixCore.PackageStore.dll | 24.00 KB |

### Self-contained + trimmed (win-x64)

| Assembly | Size |
| --- | ---: |
| msixkit.exe | 159.00 KB |
| msixkit.dll | 71.00 KB |
| MsixCore.Packaging.dll | 69.50 KB |
| msixkit.pdb | 41.09 KB |
| msixkit.deps.json | 28.87 KB |
| MsixCore.PackageStore.dll | 8.50 KB |

### Self-contained (win-arm64)

| Assembly | Size |
| --- | ---: |
| msixkit.exe | 137.00 KB |
| MsixCore.Packaging.dll | 89.00 KB |
| msixkit.dll | 74.50 KB |
| msixkit.pdb | 42.21 KB |
| msixkit.deps.json | 28.88 KB |
| MsixCore.PackageStore.dll | 24.00 KB |

### Self-contained + trimmed (win-arm64)

| Assembly | Size |
| --- | ---: |
| msixkit.exe | 137.00 KB |
| msixkit.dll | 71.00 KB |
| MsixCore.Packaging.dll | 69.50 KB |
| msixkit.pdb | 41.09 KB |
| msixkit.deps.json | 28.88 KB |
| MsixCore.PackageStore.dll | 8.50 KB |

## Comparison against the original C++ MSIX Core (future work)

The original C++ `msixkit.exe` and `MsixCore` binaries are **not** part of this
repository, so a direct size comparison cannot be produced here yet. Intended methodology:

1. Obtain an official release build of the C++ MSIX Core `msixkit.exe` (and its
   dependent DLLs) for `win-x64` from the upstream `microsoft/msix-packaging` project.
2. Record the on-disk size of the shipped executable + DLLs (the C++ build has no
   managed runtime, so its natural analogue is the **framework-dependent** column,
   while the **self-contained** column reflects the true "no prerequisites" install size).
3. Compare like-for-like: framework-dependent .NET vs. C++ needing the OS CRT/redist;
   self-contained/trimmed .NET vs. C++ statically linked, if available.
4. Track both totals and the "core packaging" binary size (`MsixCore.Packaging.dll`
   vs. the C++ `msix.dll`) over time in this report.

> The CLI uses a source-generated `JsonSerializerContext`; trimmed self-contained publishing
> is expected to succeed. Any failed configuration is omitted above and its final diagnostics
> are printed by this script.

