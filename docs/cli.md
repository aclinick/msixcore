# `msixmgr` CLI reference

`msixmgr` is the MSIX Core (.NET) command-line tool. It reads and validates
MSIX/APPX packages cross-platform. Build it once, then invoke via `dotnet`:

```bash
dotnet build -c Release
DLL=src/msixmgr/bin/Release/net10.0/msixmgr.dll
dotnet $DLL --help
```

On Windows PowerShell:

```powershell
$DLL = "src\msixmgr\bin\Release\net10.0\msixmgr.dll"
dotnet $DLL --help
```

`<path>` may be a package **file** (`.msix`/`.appx`) or an unpacked
**directory** — both are supported transparently by every verb.

## Synopsis

```
msixmgr <verb> [options]
```

| Verb / option                          | Status        | Description |
|----------------------------------------|---------------|-------------|
| `inspect <path> [--json]`              | Implemented   | Show package identity and metadata. |
| `validate <path> [--json]`             | Implemented   | Verify integrity (block map + signature); CI exit code. |
| `-AddPackage <path>`                   | Not yet       | Install an MSIX/APPX package. |
| `-RemovePackage <fullName>`            | Not yet       | Uninstall a package by full name. |
| `-FindPackage <pattern>`               | Not yet       | Query installed packages (`*` and `?`). |
| `-Unpack <path> -Destination <dir>`    | Not yet       | Extract a package without installing. |
| `-h`, `--help`, `-?`, `/?`             | Implemented   | Show help. |
| `-v`, `--version`                      | Implemented   | Show version. |

> The four deployment verbs are advertised in `--help` but currently return exit
> code `2` (`verb ... is not implemented yet`). They land in later phases.

## Exit codes

| Code | Meaning |
|------|---------|
| `0`  | Success. For `validate`, the package passed the integrity checks. |
| `1`  | Runtime error (file not found, unreadable, malformed), **or** for `validate` the package failed integrity. |
| `2`  | Usage error (unknown verb, unknown option, missing/extra argument) or an unimplemented verb. |

`validate` is designed for CI gating: `0` = integrity OK, `1` = integrity failed.

## Global

### Help

```console
$ dotnet $DLL --help
msixmgr - MSIX Core (.NET) command-line tool

Usage:
  msixmgr <verb> [options]

Verbs (implemented incrementally):
  inspect <path> [--json]     Show package identity and metadata.
  validate <path> [--json]    Verify integrity (block map + signature); CI exit code.
  -AddPackage <path>          Install an MSIX/APPX package.
  -RemovePackage <fullName>   Uninstall a package by full name.
  -FindPackage <pattern>      Query installed packages (supports * and ?).
  -Unpack <path> -Destination <dir>
                              Extract a package without installing.

<path> may be a package file (.msix/.appx) or an unpacked directory.

Options:
  -h, --help                  Show this help.
  -v, --version               Show version information.
```

Running with no arguments prints the same help and exits `0`.

### Version

```console
$ dotnet $DLL --version
1.0.0.0
```

## `inspect`

Prints package identity and metadata. A missing/invalid block map is tolerated
(reported as `(none)`); use `validate` to check integrity.

```
msixmgr inspect <package-file-or-directory> [--json]
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
Capabilities    : internetClient, runFullTrust
Block map       : 2 files (Sha256)
```

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
  "IsSigned": false,
  "BlockMapFileCount": 2,
  "BlockMapHashMethod": "Sha256"
}
```

`BlockMapFileCount`/`BlockMapHashMethod` are omitted from JSON when the package
has no readable block map (null values are not serialized).

## `validate`

Verifies package integrity and returns a CI-friendly exit code: `0` = OK,
`1` = failed. Checks performed:

- **Block map** — every block-mapped file's uncompressed content hashes and total
  size match, and payload/block-map coverage is two-way consistent.
- **Signature** (when an `AppxSignature.p7x` is present) — CMS-envelope integrity
  and signer-subject/manifest-`Publisher` agreement.

```
msixmgr validate <package-file-or-directory> [--json]
```

### Success (unsigned package)

```console
$ dotnet $DLL validate ./Contoso.MyApp
INTEGRITY OK      Contoso.MyApp_1.2.3.4_x64__h91ms92gdsmmt
  Block map : ok (2 files)
  Signature : unsigned
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
  error: block map: 'AppxManifest.xml' File 'AppxManifest.xml': block 0 hash mismatch.
  note:  package is unsigned; integrity is self-asserted by its own block map only.
$ echo $?
1
```

### JSON output

```console
$ dotnet $DLL validate ./Contoso.MyApp --json
{
  "PackageFullName": "Contoso.MyApp_1.2.3.4_x64__h91ms92gdsmmt",
  "IsValid": true,
  "BlockMapValid": true,
  "VerifiedFileCount": 2,
  "IsSigned": false,
  "Errors": [],
  "Warnings": [
    "package is unsigned; integrity is self-asserted by its own block map only."
  ]
}
```

For a signed package, the JSON additionally reports `CmsIntegrityValid`,
`SignatureBindingVerified`, and `SignatureTrustVerified`. The latter two are
always `false` today — see the authenticity note below.

> **Integrity is not authenticity.** A passing `validate` proves the payload
> matches its own block map and, if signed, that the CMS envelope is internally
> consistent and the signer subject matches the manifest publisher. It does
> **not** verify the APPX indirect-data digest binding or the certificate trust
> chain; the text output and `Warnings` say so plainly.

## Error handling examples

Unknown option (usage error, exit `2`):

```console
$ dotnet $DLL inspect ./Contoso.MyApp --bogus
msixmgr inspect: unknown option '--bogus'.
Usage: msixmgr inspect <package-file-or-directory> [--json]
```

Missing package (runtime error, exit `1`):

```console
$ dotnet $DLL inspect ./NoSuch
msixmgr inspect: No package file or directory found at './NoSuch'.
```

Unimplemented verb (exit `2`):

```console
$ dotnet $DLL -AddPackage ./Contoso.MyApp.msix
msixmgr: verb '-AddPackage' is not implemented yet.
Run 'msixmgr --help' for usage.
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
> `msixmgr.dll` against a synthesized loose package (a manifest, a `hello.txt`
> payload, and a matching `AppxBlockMap.xml`) and its zipped `.msix` equivalent.
