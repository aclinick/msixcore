# Architecture

MSIX Core (.NET) is organized as a strict, bottom-up stack. Each layer depends
only on the layers below it and exposes a small, testable surface. The two
libraries — `MsixCore.Packaging` (reading + integrity) and `MsixCore.Deployment`
(store + lifecycle) — sit under the `msixmgr` CLI.

## Layering

```
┌──────────────────────────────────────────────────────────────────┐
│  msixmgr CLI            Program → InspectCommand / ValidateCommand │
│                         PackageOpener, Reports (text + --json)     │
├──────────────────────────────────────────────────────────────────┤
│  Deployment engine      PackageManager : IPackageManager          │
│  (MsixCore.Deployment)  PackageExtractor, MsixResponse            │
│                         IMsixResponse / InstallationStep          │
│                         IPackageHandler pipeline (add/remove)      │
├──────────────────────────────────────────────────────────────────┤
│  Package store / query  IPackageStore → FileSystemPackageStore    │
│  (MsixCore.Deployment)  (writable: staging + transactional Commit)│
│                         InstalledPackage : IInstalledPackage      │
│                         Wildcard (glob matching)                  │
├──────────────────────────────────────────────────────────────────┤
│  Integrity              BlockMapParser / BlockMapVerifier         │
│  (MsixCore.Packaging)   PackageSignatureReader → PackageSignature │
├──────────────────────────────────────────────────────────────────┤
│  Manifest / identity    AppxManifestParser → AppxManifest         │
│  (MsixCore.Packaging)   BundleManifestParser, PackageIdentity,    │
│                         PublisherHash (family/full name)          │
├──────────────────────────────────────────────────────────────────┤
│  OPC container          IOpcPackage                               │
│  (MsixCore.Packaging)   OpcPackage (ZIP) / DirectoryOpcPackage    │
│                         OpcPartNames                              │
└──────────────────────────────────────────────────────────────────┘
```

```mermaid
flowchart TD
    CLI[msixmgr CLI] --> PKG[MsixPackage]
    CLI --> PM[PackageManager]
    CLI --> EXT[PackageExtractor]
    PM --> STORE[IPackageStore / FileSystemPackageStore]
    PM --> EXT
    PM --> RESP[MsixResponse : IMsixResponse]
    STORE --> INST[InstalledPackage]
    INST --> PKG
    PM -. add/remove .-> HANDLERS[IPackageHandler pipeline]
    EXT --> OPC
    PKG --> MANI[AppxManifestParser -> AppxManifest]
    PKG --> BM[BlockMapParser -> BlockMap]
    PKG --> SIG[PackageSignatureReader -> PackageSignature]
    PKG --> VERIFY[BlockMapVerifier]
    MANI --> ID[PackageIdentity + PublisherHash]
    PKG --> OPC[IOpcPackage]
    OPC --> ZIP[OpcPackage - ZIP]
    OPC --> DIR[DirectoryOpcPackage - loose]
```

`MsixPackage` is the façade that stitches the reader layers together: it owns an
`IOpcPackage`, lazily parses the manifest and block map, and exposes
`VerifyBlockMap()` and `ReadSignature()`.

## Layer 1 — OPC container (`MsixCore.Packaging.Opc`)

Every MSIX/APPX package (and bundle) is an Open Packaging Conventions ZIP
container. `IOpcPackage` is the low-level, format-agnostic reader:

- **`OpcPackage`** — backed by `System.IO.Compression.ZipArchive`. It indexes
  entries by part name (case-insensitive), rejecting invalid or duplicate part
  names up front. Read-only and **not** thread-safe (callers must synchronize).
- **`DirectoryOpcPackage`** — backed by a filesystem directory holding an
  unpacked ("loose") layout. Part names are the root-relative file paths with
  forward slashes. This is what makes loose inspection/validation/registration
  possible cross-platform.
- **`OpcPartNames`** — well-known part-name constants (`AppxManifest.xml`,
  `AppxBlockMap.xml`, `AppxSignature.p7x`, `AppxMetadata/AppxBundleManifest.xml`,
  `[Content_Types].xml`).

Both implementations share `OpcPackage.IsValidPartName` and
`OpcPackage.NormalizeLookup`, so container and loose layouts enforce identical
part-name rules.

## Layer 2 — Manifest & identity (`MsixCore.Packaging.Manifest`)

- **`AppxManifestParser`** parses `AppxManifest.xml` into an immutable
  `AppxManifest` record (identity, display/publisher names, capabilities,
  applications, target device families). Parsing is **namespace-tolerant**
  (elements matched by local name) so a single implementation spans MSIX schema
  revisions.
- **`BundleManifestParser` / `BundleManifest`** cover `.msixbundle`/`.appxbundle`
  metadata (contained application/resource packages and their qualifiers).
- **`PackageIdentity`** models the `<Identity>` element and computes the derived
  `PackageFamilyName` (`{Name}_{publisherHash}`) and `PackageFullName`
  (`{Name}_{Version}_{Architecture}_{ResourceId}_{publisherHash}`).
- **`PublisherHash`** implements the Windows publisher-hash algorithm:
  SHA-256 of the UTF-16LE publisher DN → first 8 bytes → 13-character MSIX Base32
  (`0123456789abcdefghjkmnpqrstvwxyz`). E.g. the Microsoft Store publisher hashes
  to `8wekyb3d8bbwe`.
- **`ManifestVersion`** enforces the MSIX four-part version quad (four
  components, each `0..65535`), which is stricter than `System.Version`.

## Layer 3 — Integrity (`MsixCore.Packaging.Integrity`)

- **`BlockMapParser` / `BlockMap`** parse `AppxBlockMap.xml` — per-file blocks
  (64 KiB each), base64 block hashes, and hash method (SHA-256/384/512). File
  names are normalized from the block map's native backslashes to forward
  slashes to match OPC part names.
- **`BlockMapVerifier`** re-hashes every block-mapped file's **uncompressed**
  content and checks per-block hashes, total size, and block count, then runs a
  **coverage** check: every payload part must be in the block map and vice-versa
  (excluding `AppxBlockMap.xml`, `AppxSignature.p7x`, and `[Content_Types].xml`).
  It is pure managed code, so it gates packages on Linux CI.
- **`PackageSignatureReader` / `PackageSignature`** read `AppxSignature.p7x`
  (stripping the `PKCX` magic), decode the PKCS#7/CMS envelope with `SignedCms`
  (OpenSSL-backed on Linux), and extract the primary signer (subject/issuer DN,
  thumbprint, validity, and the subject's raw DER bytes) plus a CMS-envelope
  integrity flag. `PackageSignature.MatchesPublisher` compares the manifest
  `Publisher` against the signer subject **by decoded RDN sequence**: it decodes
  the signer DN from the certificate's original raw subject bytes
  (`SubjectNameRawData`) — preserving exact RDN order and attribute encoding —
  then compares each RDN's OID and decoded string value. Comparing decoded values
  makes matching independent of the ASN.1 string encoding
  (`PrintableString` vs `UTF8String`); multi-valued RDNs fall back to a raw-bytes
  comparison. This replaces the earlier whole-DN raw-byte compare, which produced
  false mismatches on encoding or ordering differences.

> **Integrity ≠ authenticity.** `PackageSignature.IsCmsIntegrityValid` asserts
> only that the CMS envelope is internally consistent. The tool does **not** yet
> verify the APPX indirect-data digest binding (that the signature is bound to
> *this* package's block map/manifest) nor the certificate trust chain. The
> `validate` verb states this explicitly.

## Layer 4 — Package store & query (`MsixCore.Deployment`)

- **`IPackageStore`** abstracts where installed packages are recorded and where
  their unpacked payloads live. **`FileSystemPackageStore`** is a self-contained,
  cross-platform store: each installed package is a subdirectory (identified by
  the presence of `AppxManifest.xml`) under a store root, defaulting to
  `LocalApplicationData/MsixCore/Packages`. It is **writable and transactional**:
  `CreateStagingLocation()` yields a fresh `.staging/<guid>` directory the engine
  extracts into, and `Commit(staging, fullName)` promotes it with
  `Directory.Move`. Commit moves any existing install *aside* to a `.`-prefixed
  backup first, so a failed promotion **rolls back** to the previous install
  rather than destroying it; the whole aside/promote/rollback sequence is
  serialized **per destination** by a process-wide gate so concurrent commits of
  the same package cannot interleave. `.`-prefixed directories (staging, backups)
  are excluded from enumeration. `Contains`, `GetInstallLocation`, and `Delete`
  round out the surface; `GetInstallLocation` validates the full name is a single,
  non-traversing path segment.
- **`InstalledPackage`** wraps a loose `MsixPackage` and adds
  `InstalledLocation` and resolved `ExecutionInfo` (the primary app's executable
  path, safely resolved within the install root).
- **`Wildcard`** implements case-insensitive, whole-string glob matching (`*` and
  `?`) used by `FindPackages`, with a regex timeout guard.

## Layer 5 — Deployment engine (`MsixCore.Deployment`)

- **`PackageManager : IPackageManager`** implements the full lifecycle.
  `AddPackage`/`RemovePackage` return an `IMsixResponse` **immediately** and run
  the operation on a background task. `AddPackage` reads the package, gates on
  `VerifyBlockMap()` (a failing block map aborts the install), rejects an
  already-installed package unless `ForceApplicationShutdown` is set, extracts to
  a staging directory via `PackageExtractor`, then `Commit`s it — cleaning up
  staging and reporting failure on any error. The query surface (`FindPackage`,
  `FindPackageByFamilyName`, `FindPackages`, `GetMsixPackageInfo`) is unchanged,
  with careful ownership/disposal of enumerated packages.
- **`PackageExtractor`** (public, static) extracts an `IOpcPackage`'s parts to a
  directory as a loose layout. Pure managed and cross-platform, it powers both
  the `unpack` CLI verb and the install engine's extraction step. It reports
  progress (0–100), honors cancellation between chunks, and enforces the
  traversal defenses described under [Security invariants](#security-invariants).
- **`MsixResponse : IMsixResponse`** is the mutable response the engine drives
  via `Report`/`Complete`/`Fail`. It exposes a `Completion` task,
  `Percentage`/`Status` (`InstallationStep`), `StatusText`, `Failure`, a
  thread-safe `ProgressChanged` event (each subscriber invoked independently so
  one throwing observer can't strand the others or the completion task), and
  `Cancel()` backed by a linked `CancellationTokenSource`. Progress after a
  terminal transition is ignored. `SynchronousProgress<T>` delivers the engine's
  progress callbacks inline and in order.
- **`IPackageHandler` / `PackageDeploymentContext`** define the add/remove
  pipeline: handlers run in order on add and in reverse on remove, each guarded
  by `IsApplicable` so OS-integration steps (shortcuts, registry, associations)
  only run on their platform. Extraction is the cross-platform step;
  Windows OS-integration handlers land in a later phase.
- **`DeploymentOptions`** is a `[Flags]` enum (`None`, `ForceApplicationShutdown`,
  `ExtractOnly`).

## Layer 6 — CLI (`msixmgr`)

`Program.Main` dispatches verbs. `PackageOpener` transparently opens a `.msix`
file **or** a loose directory (`MsixPackage.Open` vs `MsixPackage.OpenDirectory`),
so every verb supports both layouts. `InspectCommand`, `ValidateCommand`, and
`UnpackCommand` build `record` reports (`InspectionReport`, `ValidationReport`,
`UnpackReport`) rendered as text or indented JSON (`--json`). `unpack` drives
`PackageExtractor` directly. See [cli.md](cli.md).

## Cross-platform design decisions

- **No Windows-only dependencies in the reader/integrity path.** OPC is
  `ZipArchive`, XML is `System.Xml.Linq` with a hardened reader, hashing is
  `System.Security.Cryptography`, and signatures use `SignedCms` (OpenSSL on
  Linux). This is what lets `validate` run in Linux CI.
- **Loose layouts are first-class.** `DirectoryOpcPackage` mirrors `OpcPackage`
  so inspection, validation, and the deployment store all work on unpacked
  directories without a container, and `PackageExtractor` produces such layouts
  cross-platform (powering both `unpack` and the install engine).
- **Idiomatic .NET shapes.** Native `HRESULT`/pointer interfaces become
  properties, exceptions, records, and `Task`-based async. Enum numeric values
  (`ProcessorArchitecture`) intentionally match the Windows
  `APPX_PACKAGE_ARCHITECTURE` values for faithful interop/telemetry.
- **OS integration is opt-in and guarded.** Anything platform-specific lives
  behind `IPackageHandler.IsApplicable`, keeping the libraries buildable and
  testable everywhere.

## Security invariants

- **OPC part-name canonicalization.** `OpcPackage.IsValidPartName` rejects empty,
  rooted, backslash-containing names and any `.`/`..`/empty segment; lookups are
  normalized (`NormalizeLookup`) and stored names are canonical forward-slash
  form. Duplicate (case-insensitively equal) part names are rejected, per OPC.
- **Zip-slip / path-traversal defenses.** Traversal segments are rejected at the
  part-name layer. `DirectoryOpcPackage` additionally requires each resolved file
  path to stay within the package root (`Path.GetFullPath(...).StartsWith(root)`)
  and **skips symlinks/reparse points** so a crafted loose layout cannot escape
  the root. `PackageExtractor` applies the same containment on the way *out*:
  each part must resolve under the destination, and it refuses to extract when a
  symlink/junction appears anywhere on the destination path — including the
  destination root itself or a **dangling** link (detected via no-follow
  `LinkTarget`, not `Exists`, so a broken link can't slip through and redirect a
  write). `FileSystemPackageStore` promotes only via `Directory.Move` into a
  validated single-segment install folder. `InstalledPackage.ResolveExecutionInfo`
  similarly rejects rooted or `..`-escaping executable paths from the (untrusted)
  manifest.
- **XML hardening.** All parsers (`AppxManifestParser`, `BundleManifestParser`,
  `BlockMapParser`) create readers with `DtdProcessing.Prohibit` and no external
  `XmlResolver`, blocking XXE and entity-expansion attacks.
- **Block-map integrity gating.** `BlockMapVerifier` hashes uncompressed content
  and enforces two-way coverage between payload and block map, so extra,
  missing, or tampered files fail verification. `validate` surfaces this as a
  non-zero exit code.
- **Explicit signature scope.** CMS-envelope integrity is reported but never
  conflated with authenticity; binding and trust-chain checks are explicitly
  marked unverified until implemented. Publisher matching
  (`PackageSignature.MatchesPublisher`) compares the manifest `Publisher` to the
  signer subject by **decoded RDN sequence** from the certificate's raw subject
  bytes, so it is faithful to RDN order and attribute encoding rather than
  sensitive to a lossy re-encoding.
