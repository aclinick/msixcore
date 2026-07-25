# MSIX feature-test corpus + differential test matrix

This folder is a **corpus** of small MSIX/APPX packages, each exercising a distinct MSIX
feature/manifest surface, plus a machine-readable **test matrix** (`corpus.json`) that drives the
data-driven regression suite in [`tests/MsixCore.Corpus.Tests`](../MsixCore.Corpus.Tests).

Every fixture ships in two layouts:

* a **loose** (unpacked) layout under `fixtures/<id>/` (with a hand-built `AppxBlockMap.xml`), and
* a **packed** `.msix` (or `.msixbundle`) under `packed/`.

The expected parsed values in `corpus.json` are derived **independently of the library under test**
(via `System.Xml` and an independent implementation of the MSIX publisher hash) and were
cross-checked against the **real Windows deployment oracle** (`Add-AppxPackage -Register`) at
generation time. This makes the corpus a genuine differential oracle for `MsixCore.Packaging`.

## Regenerating

```powershell
# loose + packed + corpus.json (no Windows changes)
pwsh tests/Corpus/Build-Corpus.ps1

# also sign the signable fixture with a throwaway self-signed cert (removed afterwards)
pwsh tests/Corpus/Build-Corpus.ps1 -Sign

# also validate every loose fixture against Windows and record the verdict (Developer Mode).
# Each package is registered, queried, then ALWAYS removed. Nothing is left installed.
pwsh tests/Corpus/Build-Corpus.ps1 -Sign -RunOracle
```

Prerequisites (all present on the authoring machine): `makeappx.exe`/`signtool.exe` (Windows SDK),
Developer Mode enabled, and the `Appx` PowerShell module for `-RunOracle`.

## Feature dimensions covered

| Category | Fixtures |
| --- | --- |
| Architectures | `arch-x64`, `arch-x86`, `arch-arm64`, `arch-neutral` |
| Capabilities | `cap-general` (internetClient), `cap-restricted` (rescap), `cap-device` (webcam/microphone) |
| Extensions / schema namespaces | `ext-fileassoc`, `ext-protocol`, `ext-execalias` (uap3/desktop), `ext-startuptask` (desktop), `ext-com` (com), `ext-appservice`, `ext-bgtask`, `ext-sharetarget`, `ext-contextmenu` (desktop4/com) |
| VFS content | `vfs-content` (`VFS\ProgramFilesX64`, `VFS\SystemX64`, `VFS\AppVPackageDrive`) |
| Package kinds | `kind-framework`, `kind-optional`, `kind-modification`, `kind-sparse`, `kind-resource` |
| Block-map edge cases | `blockmap-empty` (0-byte file), `blockmap-multiblock` (>64 KiB), `blockmap-percentname` (`!`, space, `+`), `blockmap-deepnested` |
| Display metadata | `meta-multiapp` (2 applications), `meta-logos` (logos + description) |
| Signing | `signed-basic` (self-signed) |
| Bundles | `bundle-multiarch` (`.msixbundle` with x64 + x86) |

## Known differential result — issue #7

`blockmap-percentname` **packed** reproduces
[issue #7](https://github.com/aclinick/msixcore/issues/7): the OPC ZIP stores part names
percent-encoded (`bang%21.txt`) while the block map uses the decoded logical name (`bang!.txt`).
On `main` the reader does not percent-decode part names, so `VerifyBlockMap()` false-fails. The
matrix records this with `blockMapValidPacked: false` and `packedKnownBug: "#7"`, and the test
asserts the current (buggy) behavior so the suite stays green while the gap remains visible. The
**loose** variant passes, because the file names on disk are already decoded.

## Bundles

`bundle-multiarch` is included so future phases have a fixture, but bundle applicability is not
implemented in the reader. `expectedSupported` is `false`; the test asserts the current documented
behavior (the container opens and carries `AppxMetadata/AppxBundleManifest.xml`, but reading it as
an app manifest throws `InvalidDataException`).
