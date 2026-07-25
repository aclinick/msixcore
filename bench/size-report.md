# msixmgr published-size report

Generated: 2026-07-24 18:46:05 -07:00

- Runtime identifier: `win-x64`
- Configuration: `Release`
- .NET SDK: `10.0.300`
- Host OS: `Microsoft Windows 10.0.26300`

## Totals

| Configuration | Total size | Files | msixmgr host |
| --- | ---: | ---: | ---: |
| Framework-dependent (portable) | 718.86 KB | 13 | 159.00 KB |
| Self-contained (win-x64) | 77.06 MB | 200 | 159.00 KB |

## Key assemblies (per configuration)

### Framework-dependent (portable)

| Assembly | Size |
| --- | ---: |
| msixmgr.exe | 159.00 KB |
| MsixCore.Packaging.dll | 62.50 KB |
| msixmgr.dll | 23.00 KB |
| msixmgr.pdb | 15.25 KB |
| MsixCore.Deployment.dll | 13.50 KB |
| msixmgr.xml | 3.30 KB |

### Self-contained (win-x64)

| Assembly | Size |
| --- | ---: |
| msixmgr.exe | 159.00 KB |
| MsixCore.Packaging.dll | 62.50 KB |
| msixmgr.deps.json | 28.78 KB |
| msixmgr.dll | 23.00 KB |
| msixmgr.pdb | 15.25 KB |
| MsixCore.Deployment.dll | 13.50 KB |

## Comparison against the original C++ MSIX Core (future work)

The original C++ `msixmgr.exe` and `MsixCore` binaries are **not** part of this
repository, so a direct size comparison cannot be produced here yet. Intended methodology:

1. Obtain an official release build of the C++ MSIX Core `msixmgr.exe` (and its
   dependent DLLs) for `win-x64` from the upstream `microsoft/msix-packaging` project.
2. Record the on-disk size of the shipped executable + DLLs (the C++ build has no
   managed runtime, so its natural analogue is the **framework-dependent** column,
   while the **self-contained** column reflects the true "no prerequisites" install size).
3. Compare like-for-like: framework-dependent .NET vs. C++ needing the OS CRT/redist;
   self-contained/trimmed .NET vs. C++ statically linked, if available.
4. Track both totals and the "core packaging" binary size (`MsixCore.Packaging.dll`
   vs. the C++ `msix.dll`) over time in this report.

> Note: trimmed self-contained size depends on trimming succeeding for the CLI. On this
> repository the trimmed configuration currently FAILS to publish: the `inspect` and
> `validate` verbs use reflection-based `System.Text.Json.JsonSerializer.Serialize`, which
> raises trim-analysis errors IL2026 (warnings-as-errors). Making the CLI trim-safe (source-
> generated `JsonSerializerContext`) would unlock a materially smaller self-contained size.

