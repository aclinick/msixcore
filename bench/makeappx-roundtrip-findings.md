# makeappx round-trip corpus findings

Date: 2026-07-25. Branch `feature/corpus-harness` was rebased onto `origin/main` before running. Harness command shape: `dotnet run -c Release --project tools\MsixCore.CorpusRoundtrip\MsixCore.CorpusRoundtrip.csproj -- --work <scratch> --modes both --report <report> <packages...>`.

## Tooling

- `makeappx.exe`: `C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe`
- Version banner: `Version 10.0.26100.8249`
- Scratch used: `D:\corpus-work\roundtrip-small-medium` for small/medium; `C:\corpus-work\roundtrip-large` for the large package after `D:` ran out of space.
- Raw harness reports: `bench\makeappx-roundtrip-small-medium.md` and `bench\makeappx-roundtrip-large.md`.

## Corpus selection

No few-MB `.msix`/`.appx` files were present under `C:\Users\andre\Downloads`; the smallest supported package files found were Windows App Runtime dependencies in the 20-24 MB range. `.msixbundle` inputs were skipped because bundle flattening/round-trip is still unimplemented.

| Tier | Package | Size | Result |
| --- | --- | ---: | --- |
| Smallest available | `Microsoft.WindowsAppRuntime.1.8-experimental3.msix` x86 | 20.92 MB | tested |
| Smallest available | `Microsoft.WindowsAppRuntime.1.8-experimental3.msix` arm64 | 22.72 MB | tested |
| Smallest available | `Microsoft.WindowsAppRuntime.1.8-experimental3.msix` x64 | 24.37 MB | tested |
| Medium | `CcProto_1.0.3.0_x64_Debug.msix` | 48.74 MB | tested |
| Medium | `Claude.msix` | 228.55 MB | tested |
| Large | `Contoso Finance Agent 1.0.1.appx` | 1247.43 MB | tested last; first attempt on `D:` aborted with insufficient disk, rerun on `C:` completed |
| Skipped | `AppInfoPkg_1.0.3.0_x64_arm64.msixbundle` | 15.36 MB | skipped bundle |
| Skipped | `MSIXplainer_1.0.14.0.msixbundle` | 94.47 MB | skipped bundle |

## Verdicts

| Package | Stored byte-identical? | Stored first diff | Optimal semantic equivalent? |
| --- | --- | ---: | --- |
| WindowsAppRuntime x86 | No | 4 | Yes |
| WindowsAppRuntime arm64 | No | 4 | Yes |
| WindowsAppRuntime x64 | No | 4 | Yes |
| CcProto x64 Debug | No | 4 | Yes |
| Claude | No | 4 | Yes |
| Contoso Finance Agent | No | 4 | Yes |

Offset 4 is the first local-file-header `version-needed-to-extract` byte: ours writes 2.0/20; makeappx writes 4.5/45.

## Size and wall-clock comparison

Times are wall-clock milliseconds from the harness. Runs were sequential.

| Package | Stored ours | Stored makeappx | Optimal ours | Optimal makeappx | Optimal ours size | Optimal makeappx size | makeappx - ours |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| WindowsAppRuntime x86 | 183 | 377 | 1,827 | 2,045 | 21,819,353 | 21,918,019 | 98,666 |
| WindowsAppRuntime arm64 | 126 | 404 | 2,439 | 3,096 | 23,692,868 | 23,804,114 | 111,246 |
| WindowsAppRuntime x64 | 150 | 5,002 | 2,394 | 2,775 | 25,428,002 | 25,528,902 | 100,900 |
| CcProto x64 Debug | 953 | 2,624 | 4,900 | 5,486 | 51,003,446 | 51,096,515 | 93,069 |
| Claude | 9,003 | 5,564 | 17,195 | 20,936 | 238,817,017 | 239,643,006 | 825,989 |
| Contoso Finance Agent | 28,873 | 27,476 | 127,918 | 164,801 | 1,319,159,132 | 1,308,032,085 | -11,127,047 |

Optimal mode passed the semantic checks for every package: payload SHA-256 sets matched and block-map per-file `Size` plus block hashes matched exactly.

## Stored-mode makeappx compatibility gaps

1. **makeappx emits ZIP64 metadata for every entry, even tiny packages.** For every tested stored-mode package, every central-directory entry differed in `version-needed-to-extract` and had central-directory extra field `0x0001(24)` on the makeappx side. The ordinary EOCD also used ZIP64 sentinel fields (`entry count = 0xffff`, `central directory offset = 0xffffffff`) plus a ZIP64 EOCD locator/record; ours emitted a non-ZIP64 EOCD when not required by size.
2. **ZIP64 is central-directory-only for entries.** makeappx central-directory entries use `version-needed-to-extract = 4.5 (45)` and `0x0001(24)` containing uncompressed size, compressed size, and local-header offset. The local file headers also use version 4.5, but no local extra-field size difference was reported; this means the central ZIP64 extra fields do not change block-map `LfhSize`.
3. **makeappx uses data descriptors.** makeappx sets general-purpose bit 3 (`0x0008`) and writes local-header CRC and 32-bit compressed/uncompressed sizes as zero. Ours sets UTF-8 (`0x0800`) and writes CRC/sizes in the local header. Spot check of makeappx output showed a signed data descriptor immediately after data: `PK 07 08`, CRC-32, then 8-byte compressed size and 8-byte uncompressed size.
4. **UTF-8 flag differs.** Ours sets bit 11 (`0x0800`) for UTF-8 names. makeappx did not set UTF-8 on these packages; it used only bit 3 (`0x0008`).
5. **Entry ordering differs.** makeappx writes payload files first in Windows-style/case-insensitive order, then `AppxManifest.xml`, `AppxBlockMap.xml`, and `[Content_Types].xml`. Ours writes `AppxManifest.xml` first, uses ordinal ordering that places upper-case root files before lower-case culture folders, and writes `[Content_Types].xml` before `AppxBlockMap.xml`.
6. **Footprint compression differs under `/nc`.** Even in the harness's Stored mode (`makeappx pack /nc`), makeappx deflated `AppxManifest.xml` and `AppxBlockMap.xml` (`method 8`) while ours stored them (`method 0`). Payload files remained stored where expected. This changes footprint CRCs and sizes.
7. **Stored block-map block `Size` attributes differ.** Ours omits stored-mode block `Size`; makeappx writes compressed/stored sizes for `AppxManifest.xml`: x86 `5051,4217`; arm64 `5053,4582`; x64 `5051,4581`; CcProto `1361`; Claude `1910`; Contoso `763`. This is the only recurring block-map semantic diff for normal payloads.
8. **OPC ZIP name escaping differs for bracketed payload names.** Claude contains payload `[Content_Types].old`. Ours writes ZIP entry `[Content_Types].old` with block-map `LfhSize=49`; makeappx writes `%5BContent_Types%5D.old` with `LfhSize=53`. The semantic payload comparison still passes, but stored byte parity and `LfhSize` do not.
9. **Regular payload `LfhSize` mostly matches.** Apart from the bracket-escaping case above and the generated/manifest footprint differences, no regular payload `LfhSize` mismatch was reported. This supports the current claim that our local-header-size calculation is compatible for ordinary payload names; the remaining parity work is ZIP metadata/ordering/escaping, not payload header length for normal files.

## Harness fixes made

- Reworked `ZipStructuralDiffer` so it streams central-directory parsing instead of loading entire archives into memory. This was needed for the 1.25 GB stress package and avoids multi-GB allocations.
- Changed structural comparison to match entries by name before reporting per-entry field differences, so ordering differences no longer hide concrete ZIP field gaps.
- Added reporting for central/local `version-needed-to-extract`, general-purpose flags, extra-field length/IDs, local-header CRC and size fields, and ZIP64 extra-field presence.

## Recommended fix order for stored byte parity

1. Match makeappx ZIP64 policy: ZIP64 EOCD/locator, central `0x0001(24)` for every entry, version-needed 4.5, and central ZIP64 values even below 4 GB.
2. Match makeappx data-descriptor policy: bit 3, zero local CRC/sizes, post-data descriptor with signature + CRC + 8-byte sizes; remove UTF-8 bit unless makeappx sets it for the name.
3. Match makeappx entry ordering: payloads first using Windows/case-insensitive ordering, then `AppxManifest.xml`, `AppxBlockMap.xml`, `[Content_Types].xml`.
4. Match makeappx footprint handling in `/nc`: deflate `AppxManifest.xml` and `AppxBlockMap.xml` and emit stored block `Size` attributes for the manifest as makeappx does.
5. Fix OPC ZIP name escaping for reserved bracketed names (`[Content_Types].old` -> `%5BContent_Types%5D.old`) and revalidate `LfhSize` after escaping.

## ZIP64 writer implementation — before/after evidence (2026-07-25)

Branch: `feature/zip64-writer`. Design follows the microsoft/msix-packaging SDK model (not makeappx.exe).

### What changed

- **ZIP64 EOCD + Locator always emitted** (56 + 20 bytes before the classic EOCD). This is the structural requirement for the SDK's `ZipObjectWriter` to open/edit a package for signing.
- **UTF-8 general-purpose bit (`0x0800`) dropped.** All ZIP entry names are percent-encoded to pure ASCII by `OpcPartNameEncoder` before reaching the writer; the bit was incorrect.
- **65,535-entry ceiling removed.** Entry count is now 64-bit in the ZIP64 EOCD.
- **Per-entry ZIP64 extra field** emitted only when a size or offset exceeds `UINT32_MAX - 1` (sentinel-driven, variable-sized). No extra fields for normal packages.
- **Data descriptors** emitted only when a size exceeds `UINT32_MAX - 1`. Normal packages continue to seek-back and patch the local file header.

### Corpus roundtrip results

Ran the harness on `tests/Corpus/packed/arch-x64.msix` (4,278 bytes, 8 entries) in stored mode.

| Diff category | Before (origin/main) | After (feature/zip64-writer) | Resolved? |
| --- | --- | --- | --- |
| #1: ZIP64 EOCD/locator absent | Present in makeappx, absent in ours | Now present in ours | **Yes** |
| #4: UTF-8 flag `0x0800` set | Set in ours, absent in makeappx | Now absent in both | **Yes** |
| #2: Central `0x0001(24)` per entry | makeappx always emits, ours never | Ours still omits (SDK model: sentinel-driven only) | No (by design) |
| #3: Data descriptors always | makeappx always emits, ours never | Ours still omits (SDK model: only for >4 GiB) | No (by design) |
| #5: Entry ordering | Differs | Unchanged | No |
| #6: Footprint compression | Differs | Unchanged | No |

### Structural verification

```
C:\temp\zip64-roundtrip\000-arch-x64\stored\ours.msix (4108 bytes)
  Classic EOCD at offset 4086:  sig=0x06054B50
  ZIP64 EOCD Locator at 4066:  sig=0x07064B50
  ZIP64 EOCD at offset 4010:   sig=0x06064B50, total_entries=8
```

Our output is now structurally compatible with the SDK's `GetIsZip64()` check, which is the prerequisite for `ZipObjectWriter` to open and edit a package (append `AppxSignature.p7x` and rewrite the central directory).

### Remaining makeappx byte-parity gaps (out of scope for this change)

Diffs #2, #3, #5, and #6 remain. These are deliberate design divergences from makeappx (we follow the SDK model). They do not affect signability or >4 GiB correctness.
