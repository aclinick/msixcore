# MSIX Core (.NET)

A cross-platform C# (.NET 10) port and modernization of Microsoft's
[MSIX Core (`msixmgr`)](https://github.com/microsoft/msix-packaging/tree/master/MsixCore).

The original MSIX Core was a C++ downlevel installer that let older Windows
releases (Windows 7 SP1+, Server 2012+) install MSIX packages. It was intended
to become the literal *core* of MSIX but never did. This project re-imagines it
as a modern, memory-safe, **cross-platform** library and CLI.

## Why

- **Cross-platform, memory-safe MSIX tooling.** Package reading, validation,
  extraction, and unsigned authoring are pure managed code on top of
  `System.IO.Compression`, `System.Xml`, and `System.Security.Cryptography` — no
  Windows-only APIs — so it runs on Linux, macOS, and Windows alike.
- **Linux CI validation.** `msixkit validate` verifies block-map integrity and
  signature-envelope integrity (an integrity verdict, not an authenticity
  verdict) and returns a CI-friendly exit code, so a Linux build agent can gate
  MSIX packages before they ship.
- **Loose (unpacked) registration.** Every reader and the deployment query
  surface work equally on a `.msix`/`.appx` container **or** an unpacked
  directory, enabling loose-layout inspection and registration.
- **Idiomatic .NET.** The native `IPackage` / `IPackageManager` / `IMsixResponse`
  interfaces are reshaped to properties, exceptions, `Task`-based async, and
  records instead of `HRESULT`s and raw pointers.

## Scope & non-goals

MSIX Core's intended signing pipeline is: **pack** (MSIX Core, cross-platform,
deterministic, unsigned output) -> **sign** (Windows job, external SignTool or CI
signing service) -> **validate** (MSIX Core, cross-platform integrity gate).

- **Code signing / signature production is a non-goal.** MSIX Core does not
  produce signatures and will not implement cross-platform CMS/AX* signature
  production. Signing is intentionally delegated to Windows SignTool/signcode and
  CI/CD signing services such as Azure Trusted Signing / Artifact Signing,
  DigiCert KeyLocker, SSL.com eSigner, and SignPath.
- **Certificate trust-chain and revocation evaluation are non-goals.** Trust is
  environment- and policy-dependent, so chain, root, and revocation decisions are
  delegated to the platform/signing environment.
- **Windows OS integration is a later phase, not a signing feature.** Shortcuts,
  registry entries, and file-type/protocol associations remain guarded,
  platform-specific deployment work.

## Components

- **`MsixCore.Packaging`** — cross-platform package reading and unsigned
  authoring: OPC/ZIP container (`OpcPackage` / `DirectoryOpcPackage`),
  `AppxManifest.xml` parsing
  (`AppxManifestParser`), block-map and signature integrity (`BlockMapVerifier`,
  `PackageSignatureReader`), identity (`PackageFullName` /
  `PackageFamilyName`), and `MsixPackageBuilder`.
- **`MsixCore.PackageStore`** — install/uninstall/query engine over an
  `IPackageStore`, with cross-platform payload extraction (`PackageExtractor`) and a
  *planned* handler pipeline (interfaces defined, not yet wired).
  `PackageManager.AddPackage`/`RemovePackage` are implemented (transactional install
  with rollback); OS integration (shortcuts, registry, file-type associations) is a
  later phase.
- **`msixkit`** — command-line tool. `inspect`, `validate`, `unpack`, and `pack` are
  implemented. Deployment operations remain available through the
  `MsixCore.PackageStore` library until dedicated CLI verbs are implemented.

## Status

Under active, **phased** development. Each phase lands as its own reviewed PR
with full test coverage (currently 370 passing tests). The reader
(OPC → manifest → block map → signature → identity), the deployment **engine**
(transactional add/remove driving `IMsixResponse`, cross-platform extraction,
and query), package/bundle authoring, and the `unpack`/`pack`/`bundle` CLI verbs are
implemented; Windows OS-integration handlers (shortcuts, registry,
associations) are guarded and land in a later phase. Authoring intentionally
produces unsigned `.msix` packages with deterministic Stored output by default
and opt-in MakeAppx-compatible 64 KiB block DEFLATE compression. It also
produces deterministic `.msixbundle`/`.appxbundle` containers from completed
packages; signing is explicitly out of scope and delegated to SignTool/signcode
or CI/CD code-signing services.

## Requirements

- .NET 10 SDK (pinned in `global.json`; `10.0.100`, `rollForward: latestMajor`).
- A 64-bit host: **x64 or arm64**. 32-bit hosts are not supported, since 64-bit
  Windows is the default everywhere the tools run. The tools always run
  **native** — an arm64 machine runs the arm64 build, never the emulated x64 one.

This is a statement about the machine running the tools, **not** about the
packages they handle. `x86` remains a fully supported *package* architecture:
packages that are x86, or that carry x86 binaries, are read, validated, authored
and resolved exactly like any other. A 64-bit Windows machine installs x86
packages, so an x86-only bundle still resolves against an x64 or arm64 target.

## Build & test

```bash
dotnet build -c Release
dotnet test --configuration Release
```

The solution uses the XML-based `.slnx` format (`MsixCore.slnx`) and builds
warning-free with `TreatWarningsAsErrors` enabled.

## Quick start (CLI)

Build once, then invoke the tool via `dotnet`:

```bash
dotnet build -c Release
DLL=src/msixkit/bin/Release/net10.0/msixkit.dll
```

`<path>` may be a `.msix`/`.appx` file **or** an unpacked directory.

### inspect — identity and metadata

```console
$ dotnet $DLL inspect ./Contoso.MyApp
Name            : Contoso.MyApp
Full name       : Contoso.MyApp_1.2.3.4_x64__h91ms92gdsmmt
Family name     : Contoso.MyApp_h91ms92gdsmmt
Version         : 1.2.3.4
Architecture    : x64
Display name    : Contoso My App
Publisher       : Contoso Ltd
Signed          : False
Capabilities    : internetClient, runFullTrust
Block map       : 2 files (Sha256)
```

Add `--json` for machine-readable output.

### validate — integrity gate (CI-friendly exit code)

```console
$ dotnet $DLL validate ./Contoso.MyApp
INTEGRITY OK      Contoso.MyApp_1.2.3.4_x64__h91ms92gdsmmt
  Block map : ok (2 files)
  Signature : unsigned
  note:  package is unsigned; integrity is self-asserted by its own block map only.
$ echo $?
0
```

`validate` is an integrity gate, not an authenticity guarantee: a valid CMS
envelope only proves the signature envelope is internally consistent; MSIX Core
does not verify APPX indirect-data binding or certificate trust chains.

A tampered payload fails the block-map check and returns exit code `1`:

```console
$ dotnet $DLL validate ./Corrupt
INTEGRITY FAILED  Contoso.MyApp_1.2.3.4_x64__h91ms92gdsmmt
  Block map : FAILED (2 files)
  Signature : unsigned
  error: block map: 'AppxManifest.xml' File 'AppxManifest.xml': block 0 hash mismatch.
```

### unpack — extract without installing

```console
$ dotnet $DLL unpack ./Contoso.MyApp.msix -Destination ./out
Extracted 4 parts to /abs/path/to/out
```

Extraction is cross-platform and hardened against zip-slip and symlink/junction
escapes. See [docs/cli.md](docs/cli.md).

## Project layout

```
MsixCore.slnx                 XML solution (src + tests)
Directory.Build.props         Shared build config (net10.0, warnings-as-errors)
global.json                   Pinned .NET 10 SDK
src/
  MsixCore.Packaging/         Cross-platform package reader (OPC, manifest, integrity)
    Opc/                      OpcPackage, DirectoryOpcPackage, OpcPartNames
    Manifest/                 AppxManifestParser, BundleManifestParser, models
    Integrity/                BlockMapVerifier, PackageSignatureReader
  MsixCore.PackageStore/        Install/uninstall/query engine + IPackageStore
    Handlers/                 IPackageHandler pipeline
  msixkit/                    CLI (inspect, validate, ...)
tests/
  MsixCore.Packaging.Tests/
  MsixCore.PackageStore.Tests/
  msixkit.Tests/
.github/workflows/ci.yml      Build & test on ubuntu-latest + windows-latest
```

## Documentation

See the [`docs/`](docs/) folder:

- [Architecture](docs/architecture.md) — layering, key types, cross-platform and
  security design.
- [`msixkit` CLI reference](docs/cli.md) — every verb, options, exit codes, and
  text/JSON output.
- [Public API reference](docs/api.md) — `MsixCore.Packaging` and
  `MsixCore.PackageStore` surface.
- [Contributing](docs/contributing.md) — build/test, conventions, and the
  branch → PR → review → merge workflow.

## License

MIT — see [LICENSE](LICENSE).
