---
name: msix
description: >-
  Inspect, validate, unpack, and check dependencies of MSIX/APPX packages with the cross-platform
  MSIX Core (.NET 10) library and its `msixkit` CLI. USE FOR: reading a package's identity/manifest
  (name, publisher, version, architecture, package family/full name, capabilities, applications);
  verifying integrity in CI (block-map hash + coverage, and CMS signature envelope) with a
  CI-friendly exit code; extracting a `.msix`/`.appx` (or loose folder) to disk without installing;
  resolving whether a package's declared dependencies are satisfied;
  reading signature/signer details; MSIX in Linux/macOS CI/CD pipelines; consuming the
  `MsixCore.Packaging` / `MsixCore.PackageStore` NuGet libraries from C#. Works on a package FILE or an
  unpacked DIRECTORY. Sign packages with `winapp sign`. DO NOT USE FOR: building/scaffolding a WinUI 3
  app or authoring `Package.appxmanifest` (use the winui skills), creating a package from source
  with makeappx (this reads/validates/unpacks existing packages), or installing/registering a package
  on Windows (use the Windows deployment APIs - this library deliberately does not install).
---

# MSIX Core (.NET) — inspect / validate / unpack

**MSIX Core (.NET 10)** is a memory-safe, **cross-platform** port of Microsoft's MSIX Core. It ships:

- **`msixkit`** — a CLI for inspecting, validating, and unpacking MSIX/APPX packages. The read/verify/
  unpack paths run on **Windows, Linux, and macOS** (no OS integration required), so they work in CI.
- **`MsixCore.Packaging`** — the library for opening a package (container file *or* loose folder),
  reading identity/manifest/block-map/signature, and verifying integrity.
- **`MsixCore.PackageStore`** — extraction (`PackageExtractor`) and dependency resolution
  (`DependencyResolver`).

**This library does not install MSIX packages.** Windows installs MSIX natively, so an earlier
non-OS-integrated install engine was removed rather than shipped. Use `Add-AppxPackage` /
`PackageManager` from the Windows SDK to install; use this library to read, validate, author,
extract, and to decide whether a package's dependencies are satisfied.

The packaging **reads** and the `msixkit` verbs (`inspect`/`validate`/`unpack`) each accept either a
**package file** (`.msix`/`.appx`) or an **unpacked directory** (a loose layout containing
`AppxManifest.xml`).

## Quick reference (`msixkit` CLI)

| Task | Command |
|------|---------|
| Show identity + metadata | `msixkit inspect <path>` |
| Same, machine-readable | `msixkit inspect <path> --json` |
| Verify integrity (CI gate) | `msixkit validate <path>` |
| Same, machine-readable | `msixkit validate <path> --json` |
| Extract without installing | `msixkit unpack <path> -Destination <dir>` |
| Help / version | `msixkit --help` · `msixkit --version` |

`<path>` is a `.msix`/`.appx` file or an unpacked directory. **Exit codes:** `0` success/valid,
`1` runtime error or **invalid** package, `2` usage error.

Run the built CLI with `dotnet run --project src/msixkit -- <verb> ...`, or publish it and invoke
`msixkit` directly.

## `inspect` — identity & metadata

```console
$ msixkit inspect .\App.msix
Name            : Contoso.App
Full name       : Contoso.App_1.2.3.0_x64__abcd1234efgh5
Family name     : Contoso.App_abcd1234efgh5
Version         : 1.2.3.0
Architecture    : x64
Display name    : Contoso App
Publisher       : Contoso, Ltd.
Signed          : True
Capabilities    : runFullTrust, internetClient
Block map       : 42 files (SHA256)
```

`--json` emits the same fields as a JSON object (stable property names) for scripting. `inspect` still
prints identity even if the block map is missing/invalid (use `validate` to gate on integrity).

## `validate` — integrity for CI/CD

Verifies **block-map** file hashes + coverage, the **manifest**'s semantic rules (identifier form,
package-type consistency, version ranges), and, when the package is signed, the **CMS signature
envelope** integrity and that the signer subject matches the manifest `Publisher`. Returns `0` when
valid, `1` when invalid — ideal for pipeline gating.

```console
$ msixkit validate .\App.msix
INTEGRITY OK      Contoso.App_1.2.3.0_x64__abcd1234efgh5
  Block map : ok (42 files)
  Signature : CMS envelope ok (binding + trust NOT verified)
  Manifest  : ok
  note:  signature binding (APPX indirect-data digests) and certificate trust are NOT verified; this is not an authenticity guarantee.
```

> **Integrity ≠ authenticity.** `validate` proves the payload matches its block map and (if signed)
> that the CMS envelope is internally consistent. It does **not** yet verify the APPX indirect-data
> digest **binding** or the certificate **trust chain**, so a passing result is not proof the package
> is authentic/untampered by a trusted publisher. It is exposed explicitly as a `warning`/`note`.

CI example (fail the job on an invalid package):

```yaml
- run: dotnet run -c Release --project src/msixkit -- validate ./artifact/App.msix
```

## `unpack` — extract without installing

```console
$ msixkit unpack .\App.msix -Destination .\out
Extracted 45 parts to D:\work\out
```

Cross-platform and side-effect-free (no registration). The extractor contains path traversal and
symlink/reparse-point escapes (a malicious package cannot write outside `-Destination`).

## Library usage (C#)

Reference `MsixCore.Packaging` (+ `MsixCore.PackageStore` for extraction and dependency resolution).

```csharp
using MsixCore.Packaging;
using MsixCore.Packaging.Integrity;
using MsixCore.PackageStore;

// Open a package file OR a loose directory:
using MsixPackage package = MsixPackage.Open("App.msix");     // or .OpenDirectory("./loose")

// Identity & metadata
PackageIdentity id = package.Identity;                         // Name, Publisher, Version, Architecture,
                                                              // PackageFamilyName, PackageFullName, ResourceId
string display = package.DisplayName;
IReadOnlyList<string> caps = package.Capabilities;
int appCount = package.Manifest.Applications.Count;

// Integrity: verify payload against the block map
BlockMapVerificationResult result = package.VerifyBlockMap();
if (!result.IsValid) { /* result.Files[*].Error, result.CoverageErrors */ }

// Signature (null when unsigned)
if (package.ReadSignature() is { } sig)
{
    bool cmsOk   = sig.IsCmsIntegrityValid;                    // envelope integrity only (not trust)
    bool pubOk   = sig.MatchesPublisher(id.Publisher);        // signer subject == manifest Publisher
    // sig.SubjectName / IssuerName / Thumbprint / NotBefore / NotAfter
}

// Extract to a loose layout (cross-platform, no install)
PackageExtractor.Extract(package.Opc, "./out");
```

**Check whether a package's dependencies are satisfied** before handing it to a real installer:

```csharp
using MsixCore.PackageStore;

// You supply the installed set - from the Windows package inventory, a directory of
// staged packages, or your own records. Pass every package in a family, not just the newest.
IEnumerable<InstalledPackageInfo> installed = Directory
    .EnumerateDirectories("/var/lib/packages")
    .Select(InstalledPackageInfo.ReadFromDirectory);

DependencyResolutionResult result = DependencyResolver.Resolve(package.Manifest, installed);
if (!result.CanDeploy)                                     // not IsSatisfied: optional deps don't block
{
    foreach (DependencyResolution blocking in result.Blocking)
    {
        Console.Error.WriteLine(blocking.Describe());      // "framework 'X' is not installed."
    }
}
```

## Signing

Sign a built package with **winapp** (the environment's signing tool), then `validate` it:

```powershell
winapp sign .\App.msix .\devcert.pfx --password <pwd>
msixkit validate .\App.msix
```

For generating/trusting a dev certificate and packaging a WinUI app, use the **winui-packaging**
skill; this skill focuses on reading/validating/unpacking existing packages.

## Cross-platform notes

- `inspect`, `validate`, `unpack`, and all `MsixCore.Packaging` reads run identically on Windows,
  Linux, and macOS — this is the point of the .NET 10 port (great for Linux CI/CD).
- Filenames inside a package are matched per the OPC canonicalization rules (percent-decoded); the
  library is careful about case-sensitive filesystems on Linux.
- CI covers Windows and Linux. macOS is expected to work but is **not yet verified by CI**.

## Current limitations

- **Bundles** (`.msixbundle`) are recognized as containers but bundle applicability/flattening is not
  yet implemented — reading a bundle as an app package throws `InvalidDataException`.
- Signature **binding + trust chain** verification is not yet implemented (see the `validate` note).
- **Installing packages is out of scope** — use the Windows deployment APIs.

## Build & test

```powershell
dotnet build -c Release
dotnet test  -c Release
```

Targets **net10.0**, nullable enabled, warnings-as-errors.
