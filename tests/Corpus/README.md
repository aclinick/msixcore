# MSIX feature-test corpus + differential test matrix

This folder is a **corpus** of small MSIX/APPX packages, each exercising a distinct MSIX
feature/manifest surface, plus a machine-readable **test matrix** (`corpus.json`) that drives the
data-driven regression suite in [`tests/MsixCore.Corpus.Tests`](../MsixCore.Corpus.Tests).

Every fixture ships in two layouts:

* a **loose** (unpacked) layout under `fixtures/<id>/` (with a hand-built `AppxBlockMap.xml`), and
* a **packed** `.msix` (or `.msixbundle`) under `packed/`.

The expected parsed values in `corpus.json` are derived **independently of the library under test**
(via `System.Xml` and an independent implementation of the MSIX publisher hash) and were
cross-checked against the **real Windows deployment oracle** (`Add-AppxPackage`) at generation
time. This makes the corpus a genuine differential oracle for `MsixCore.Packaging`.

The manifest `Publisher` is set to the exact subject DN (`CN=aclinick`) of a **trusted
self-signed code-signing certificate** kept in the current user's *Trusted People* store. Packed
packages are signed with that certificate (via `winapp sign`), so they install as real signed
`.msix` files during oracle validation.

## Regenerating

```powershell
# loose + packed + corpus.json (no Windows changes)
pwsh tests/Corpus/Build-Corpus.ps1

# also sign every makeappx-produced package with the trusted corpus cert (winapp sign).
# The cert's private key is exported to a throwaway PFX that is deleted afterwards; the
# certificate stores themselves are never modified.
pwsh tests/Corpus/Build-Corpus.ps1 -Sign

# also validate each fixture against Windows and record the verdict (Developer Mode).
# Signed .msix packages are installed via Add-AppxPackage; unsigned ones fall back to
# loose registration. Every package is queried, then ALWAYS removed. Nothing is left installed.
pwsh tests/Corpus/Build-Corpus.ps1 -Sign -RunOracle
```

`-SignThumbprint` selects the signing cert (default `1999384EEF0362515797C62766388F94B46EA7A7`,
subject `CN=aclinick`); its subject DN becomes the manifest `Publisher`. Prerequisites (all present
on the authoring machine): `makeappx.exe`/`signtool.exe` (Windows SDK), the `winapp` CLI, Developer
Mode enabled, and the `Appx` PowerShell module for `-RunOracle`.

Packages are packed with **makeappx** (a signtool-recognized APPX) so they can be signed and
installed. Two fixtures fall back to a **self-built OPC ZIP** and therefore stay unsigned:
`blockmap-percentname` (needs percent-encoded part names to reproduce issue #7) and `ext-bgtask`
(its intentionally-invalid manifest is rejected by makeappx).

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
| Signing | `signed-basic` (trusted self-signed cert); 27 of 30 packages are signed |
| Bundles | `bundle-multiarch` (`.msixbundle` with x64 + x86) |

## Windows-oracle verdicts

At last generation (`-Sign -RunOracle`): **25 installed**, **3 expected-not-installable**
(`kind-optional`/`kind-modification` need their host package, `kind-sparse` needs an external
location), **1 failed** (`ext-bgtask` — an intentionally-invalid manifest, `0x80080204`), and
**1 not-attempted** (`bundle-multiarch`). `kind-framework` now installs because it is delivered as
a signed `.msix` (rather than dev-mode loose registration, which forbids framework packages). The
per-fixture verdict and the exact Windows error string are recorded in `corpus.json`.

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
