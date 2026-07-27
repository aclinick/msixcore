# `msixkit` CLI reference

`msixkit` is the MSIX Core (.NET) command-line tool. It reads, validates,
extracts, and authors unsigned MSIX packages and bundles cross-platform. Build it once,
then invoke via `dotnet`:

```bash
dotnet build -c Release
DLL=src/msixkit/bin/Release/net10.0/msixkit.dll
dotnet $DLL --help
```

On Windows PowerShell:

```powershell
$DLL = "src\msixkit\bin\Release\net10.0\msixkit.dll"
dotnet $DLL --help
```

For `inspect`, `validate`, and `unpack`, `<path>` may be a package **file**
(`.msix`/`.appx`) or an unpacked **directory**. `pack` specifically requires a
source directory.

## Synopsis

```
msixkit <verb> [options]
```

| Verb / option                          | Status        | Description |
|----------------------------------------|---------------|-------------|
| `inspect <path> [--json]`              | Implemented   | Show package identity and metadata. |
| `validate <path> [--json]`             | Implemented   | Verify integrity (block map + signature) and manifest semantics; CI exit code. |
| `unpack <path> -Destination <dir> [--json]` | Implemented | Extract a package to a loose layout without installing. |
| `pack <sourceDir> -o <file.msix> [--compress] [--overwrite] [--json]` | Implemented | Build an unsigned MSIX package. |
| `bundle <package.msix>... -o <file.msixbundle> [--version <a.b.c.d>] [--overwrite] [--json]` | Implemented | Build an unsigned MSIX bundle. |
| `-h`, `--help`, `-?`, `/?`             | Implemented   | Show help. |
| `-v`, `--version`                      | Implemented   | Show version. |

> Installing/registering packages is out of scope for this project — Windows installs MSIX natively —
> so there are no add/remove verbs. See [architecture.md](architecture.md).

## Exit codes

| Code | Meaning |
|------|---------|
| `0`  | Success. For `validate`, the package passed the integrity checks. |
| `1`  | Runtime error (file not found, unreadable, malformed), **or** for `validate` the package failed integrity. |
| `2`  | Usage error (unknown verb, unknown option, or missing/extra argument). |

`validate` is designed for CI gating: `0` = integrity OK, `1` = integrity failed.

## Global

### Help

```console
$ dotnet $DLL --help
msixkit - MSIX Core (.NET) command-line tool

Usage:
  msixkit <verb> [options]

Verbs:
  inspect <path> [--json]                                                Show package identity and metadata.
  validate <path> [--json]                                               Verify integrity (block map + signature); CI exit code.
  unpack <path> -Destination <dir> [--json]                              Extract a package to a loose layout without installing.
  pack <sourceDir> -o|--output <file.msix> [--compress] [--overwrite] [--json]  Build an unsigned MSIX package.
  bundle <package.msix>... -o|--output <file.msixbundle> [--version <a.b.c.d>] [--overwrite] [--json]  Build an unsigned MSIX bundle.

For inspect, validate, and unpack, <path> may be a package file
(.msix/.appx) or an unpacked directory. pack requires a source directory;
bundle requires one or more already-built package files.

Options:
  -h, --help                  Show this help.
  -v, --version               Show version information.
```

Running with no arguments prints the same help and exits `0`.

### Version

```console
$ dotnet $DLL --version
0.1.0.0
```

## `inspect`

Prints package identity and metadata. A missing/invalid block map is tolerated
(reported as `(none)`); use `validate` to check integrity.

```
msixkit inspect <package-file-or-directory> [--json]
```

### Text output

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
Capabilities    : internetClient, runFullTrust (restricted), location (device)
Dependencies    :
  framework   Microsoft.VCLibs.140.00 >= 14.0.30704.0
Extensions      :
  [App] windows.fileTypeAssociation contoso-doc: .cdoc
Block map       : 2 files (Sha256)
```

A capability is annotated with its category when it is anything other than a plain general-use
capability — `restricted`, `windows`, `device`, `custom`, or `unknown`. The category comes from the
XML namespace the capability was declared with, not from its name: `runFullTrust` is restricted
because it must be declared as `<rescap:Capability>`.

The `Dependencies` and `Extensions` sections are printed only when the package declares any. An
extension is tagged with the id of the declaring application, or `package` for a package-level
extension. See [manifest dependencies](manifest-dependencies.md) and
[manifest extensions](manifest-extensions.md).

### JSON output

```console
$ dotnet $DLL inspect ./Contoso.MyApp --json
{
  "Name": "Contoso.MyApp",
  "PackageFullName": "Contoso.MyApp_1.2.3.4_x64__h91ms92gdsmmt",
  "PackageFamilyName": "Contoso.MyApp_h91ms92gdsmmt",
  "Version": "1.2.3.4",
  "Architecture": "x64",
  "DisplayName": "Contoso My App",
  "PublisherDisplayName": "Contoso Ltd",
  "Capabilities": [
    "internetClient",
    "runFullTrust"
  ],
  "DeclaredCapabilities": [
    {
      "Name": "internetClient",
      "Kind": "general",
      "Namespace": "http://schemas.microsoft.com/appx/manifest/foundation/windows10"
    },
    {
      "Name": "runFullTrust",
      "Kind": "restricted",
      "Namespace": "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
    }
  ],
  "Dependencies": [
    {
      "Kind": "framework",
      "Name": "Microsoft.VCLibs.140.00",
      "Publisher": "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US",
      "MinVersion": "14.0.30704.0",
      "IsOptional": false
    }
  ],
  "Extensions": [
    {
      "ApplicationId": "App",
      "Category": "windows.fileTypeAssociation",
      "Details": "contoso-doc: .cdoc"
    }
  ],
  "IsSigned": false,
  "BlockMapFileCount": 2,
  "BlockMapHashMethod": "Sha256"
}
```

`BlockMapFileCount`/`BlockMapHashMethod` are omitted from JSON when the package
has no readable block map (null values are not serialized).

`Capabilities` remains the flat, de-duplicated list of names it has always been; `DeclaredCapabilities`
is the additive, categorized view, in document order and without de-duplication. One behaviour did
change: a recognised capability element with no `Name` is now a hard error rather than being silently
ignored. An element the parser does not recognise is still reported when it carries a `Name`, and
still ignored when it does not.

## `validate`

Verifies package integrity and returns a CI-friendly exit code: `0` = OK,
`1` = failed. Checks performed:

- **Block map** — every block-mapped file's uncompressed content hashes and total
  size match, and payload/block-map coverage is two-way consistent.
- **Signature** (when an `AppxSignature.p7x` is present) — CMS-envelope integrity
  and signer-subject/manifest-`Publisher` agreement.
- **Manifest** — semantic rules Windows enforces at deployment time: identifier
  form, publisher shape, package-type consistency, and version ranges. See
  [manifest validation](manifest-validation.md) for the rule list. An unknown XML
  namespace is a *warning* and does not fail the gate.

```
msixkit validate <package-file-or-directory> [--json]
```

### Success (unsigned package)

```console
$ dotnet $DLL validate ./Contoso.MyApp
INTEGRITY OK      Contoso.MyApp_1.2.3.4_x64__h91ms92gdsmmt
  Block map : ok (1 files)
  Signature : unsigned
  Manifest  : ok
  note:  package is unsigned; integrity is self-asserted by its own block map only.
$ echo $?
0
```

### Failure (tampered payload)

```console
$ dotnet $DLL validate ./Corrupt
INTEGRITY FAILED  Contoso.MyApp_1.2.3.4_x64__h91ms92gdsmmt
  Block map : FAILED (2 files)
  Signature : unsigned
  Manifest  : ok
  error: block map: 'AppxManifest.xml' File 'AppxManifest.xml': block 0 hash mismatch.
  note:  package is unsigned; integrity is self-asserted by its own block map only.
$ echo $?
1
```

### Failure (invalid manifest)

```console
$ dotnet $DLL validate ./BadIdentity
INTEGRITY FAILED  Contoso.MyApp._1.2.3.4_x64__h91ms92gdsmmt
  Block map : ok (1 files)
  Signature : unsigned
  Manifest  : FAILED (1 error)
  error: manifest: Identity/@Name — 'Contoso.MyApp.' ends with a period, which is not allowed in an identifier.
  note:  package is unsigned; integrity is self-asserted by its own block map only.
$ echo $?
1
```

### JSON output

```console
$ dotnet $DLL validate ./Contoso.MyApp --json
{
  "schemaVersion": 1,
  "PackageFullName": "Contoso.MyApp_1.2.3.4_x64__h91ms92gdsmmt",
  "IsValid": true,
  "BlockMapValid": true,
  "VerifiedFileCount": 1,
  "IsSigned": false,
  "Errors": [],
  "Warnings": [
    "package is unsigned; integrity is self-asserted by its own block map only."
  ],
  "ManifestValid": true,
  "ManifestIssues": []
}
```

Each entry in `ManifestIssues` carries `Severity` (`error` or `warning`), `Rule`
(a stable identifier such as `IdentifierReserved`), `Target`, and `Message`.

For a signed package, the JSON additionally reports `CmsIntegrityValid`,
`SignatureBindingVerified`, and `SignatureTrustVerified`. The latter two are
always `false` today — see the authenticity note below.

> **Integrity is not authenticity.** A passing `validate` proves the payload
> matches its own block map and, if signed, that the CMS envelope is internally
> consistent and the signer subject matches the manifest publisher. It does
> **not** verify the APPX indirect-data digest binding or the certificate trust
> chain; the text output and `Warnings` say so plainly.

## `unpack`

Extracts a package's payload to a directory as a loose (unpacked) layout,
**without installing** it. Cross-platform (no OS integration), so it works on
Linux CI the same as on Windows. The destination is created if missing; part
paths are reproduced under it.

```
msixkit unpack <package-file-or-directory> -Destination <dir> [--json]
```

The destination flag is accepted as `-Destination`, `-destination`,
`--destination`, or `-d`.

### Text output

```console
$ dotnet $DLL unpack ./Contoso.MyApp.msix -Destination ./out
Extracted 4 parts to /abs/path/to/out
$ echo $?
0
```

The extracted layout can be re-validated (a round-trip integrity check):

```console
$ dotnet $DLL validate ./out
INTEGRITY OK      Contoso.MyApp_1.2.3.4_x64__h91ms92gdsmmt
  Block map : ok (2 files)
  Signature : unsigned
  note:  package is unsigned; integrity is self-asserted by its own block map only.
```

### JSON output

```console
$ dotnet $DLL unpack ./Contoso.MyApp.msix -Destination ./out2 --json
{
  "Destination": "/abs/path/to/out2",
  "ExtractedPartCount": 4
}
```

`Destination` is the absolute path of the output directory; `ExtractedPartCount`
is the number of OPC parts written.

### Usage errors

A missing destination (exit `2`):

```console
$ dotnet $DLL unpack ./Contoso.MyApp.msix
msixkit unpack: a destination directory is required (-Destination <dir>).
Usage: msixkit unpack <package-file-or-directory> -Destination <dir> [--json]

$ dotnet $DLL unpack ./Contoso.MyApp.msix -Destination
msixkit unpack: option '-Destination' requires a directory argument.
Usage: msixkit unpack <package-file-or-directory> -Destination <dir> [--json]
```

Extraction is hardened against traversal: a part that would resolve outside the
destination, or a symlink/junction anywhere on the destination path (including a
dangling link, or the destination root itself), aborts extraction with exit code
`1`. See [architecture.md](architecture.md#layer-5--package-store-msixcorepackagestore).

## `pack`

Builds an unsigned `.msix` from a directory containing `AppxManifest.xml` at
its root plus payload files:

```console
$ dotnet $DLL pack ./layout -o ./Contoso.MyApp.msix
Packed 4 files (70231 bytes) to /abs/path/Contoso.MyApp.msix
Identity: Contoso.MyApp_1.2.3.4_x64__h91ms92gdsmmt
```

Existing output is rejected unless
`--overwrite` is supplied. Input `AppxBlockMap.xml`, `AppxSignature.p7x`, and
`[Content_Types].xml` files are ignored; the builder generates fresh block-map
and content-types parts and intentionally does not sign the package. Sign with
external Windows SignTool/signcode or a CI/CD signing service before
distribution. Package entries are Stored/uncompressed by default so existing
output remains byte-compatible.
`--compress` enables MakeAppx-compatible 64 KiB block DEFLATE. Each block is
restartable, hashes its uncompressed bytes, and records its compressed length;
incompressible blocks remain deflated, while MakeAppx's already-compressed
media/archive file types (such as PNG/JPEG/ZIP) remain Stored.

```console
$ dotnet $DLL pack ./layout --output ./Contoso.MyApp.msix --json
{
  "OutputPath": "/abs/path/Contoso.MyApp.msix",
  "Name": "Contoso.MyApp",
  "PackageFullName": "Contoso.MyApp_1.2.3.4_x64__h91ms92gdsmmt",
  "PackageFamilyName": "Contoso.MyApp_h91ms92gdsmmt",
  "Version": "1.2.3.4",
  "Architecture": "x64",
  "FileCount": 4,
  "TotalSize": 70231,
  "IsSigned": false,
  "Compression": "Stored"
}
```

Missing/invalid source content or an unwritable output returns exit `1`;
missing/unknown/extra arguments return exit `2`.

## `bundle`

Builds an unsigned `.msixbundle` (or `.appxbundle`) from one or more already-built
`.msix`/`.appx` packages:

```console
$ dotnet $DLL bundle ./Contoso.x64.msix ./Contoso.arm64.msix -o ./Contoso.msixbundle --version 1.2.3.4
Bundled 2 packages (140462 bytes) to /abs/path/Contoso.msixbundle
Identity: Contoso.MyApp_1.2.3.4_neutral__h91ms92gdsmmt
```

All child packages must share the same `Name` and `Publisher`, and application
packages must target distinct architectures. Child packages are stored byte-for-byte
without recompression. If `--version` is omitted, the highest child package version is
used, making the default deterministic. Existing output requires `--overwrite`.

`--json` reports the bundle identity plus every child's type, architecture/resource
ID, payload offset, and size. The generated bundle is unsigned; signing is delegated
to external Windows SignTool/signcode or CI/CD signing services.

## Error handling examples

Unknown option (usage error, exit `2`):

```console
$ dotnet $DLL inspect ./Contoso.MyApp --bogus
msixkit inspect: unknown option '--bogus'.
Usage: msixkit inspect <package-file-or-directory> [--json]
```

Missing package (runtime error, exit `1`):

```console
$ dotnet $DLL inspect ./NoSuch
msixkit inspect: No package file or directory found at './NoSuch'.
```

Unknown verb (exit `2`):

```console
$ dotnet $DLL -AddPackage ./Contoso.MyApp.msix
msixkit: unknown verb '-AddPackage'.
Run 'msixkit --help' for usage.
```

## Working on container files

The same verbs work on a `.msix`/`.appx` file. For example, validating a zipped
container produces identical output to the loose directory:

```console
$ dotnet $DLL validate ./Contoso.MyApp.msix
INTEGRITY OK      Contoso.MyApp_1.2.3.4_x64__h91ms92gdsmmt
  Block map : ok (2 files)
  Signature : unsigned
  note:  package is unsigned; integrity is self-asserted by its own block map only.
```

> All example output above was produced by running the built
> `msixkit.dll` against a synthesized loose package (a manifest, a `hello.txt`
> payload, and a matching `AppxBlockMap.xml`) and its zipped `.msix` equivalent.
