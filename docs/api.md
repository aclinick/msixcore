# Public API reference

A concise map of the public surface of `MsixCore.Packaging` and
`MsixCore.PackageStore`. Signatures are abbreviated; see the XML doc comments in
source for full details. Types are immutable `record`s unless noted.

## `MsixCore.Packaging`

### Authoring — `MsixCore.Packaging.Authoring`

`MsixPackageBuilder` creates unsigned `.msix` OPC/ZIP packages.
`MsixBundleBuilder` combines already-built packages into unsigned bundles. Both
generate `AppxBlockMap.xml` plus `[Content_Types].xml`.

| Member | Description |
|--------|-------------|
| `static PackResult Build(string sourceDirectory, string outputPath, PackOptions? = null)` | Build from a directory whose root contains `AppxManifest.xml`. |
| `MsixPackageBuilder SetManifest(Stream)` | Copy manifest bytes from the stream's current position; the caller retains ownership. |
| `MsixPackageBuilder AddFile(string packagePath, Stream)` | Copy payload bytes immediately; the caller retains ownership. |
| `MsixPackageBuilder AddFile(string packagePath, string sourcePath)` | Add a payload file that is opened when building. |
| `PackResult Build(string outputPath, PackOptions? = null)` | Build the programmatically configured package. |
| `static BundleResult MsixBundleBuilder.Build(IEnumerable<string> packagePaths, string outputPath, BundleOptions? = null)` | Build a bundle from child package paths. |
| `MsixBundleBuilder AddPackage(string packagePath)` | Add an existing `.msix`/`.appx` child. |
| `BundleResult Build(string outputPath, BundleOptions? = null)` | Build the configured bundle. |

`PackOptions.Overwrite` controls replacement of an existing output.
`PackOptions.CompressionLevel` defaults to `CompressionLevel.NoCompression`;
`CompressionLevel.Optimal` enables MakeAppx-compatible block compression.
Compressed files are split into independent 64 KiB raw-DEFLATE blocks, with
each compressed block length recorded in `AppxBlockMap.xml`; already-compressed
media/archive file types follow MakeAppx and remain Stored. The default remains
Stored to preserve existing authored-package bytes.
`PackResult` reports the absolute output path, manifest `Identity`, block-mapped
file count, total uncompressed payload size, and compression level. The
completed package is read back and block-map verified before it replaces the
requested output.

`BundleOptions.Version` sets the bundle identity version; when omitted, the
highest child version is used deterministically. `BundleResult` reports the
bundle identity and child entries, including type, architecture/resource ID,
payload offset, and size. Builders author only; they intentionally do not sign
output. Sign with external Windows SignTool/signcode or CI/CD signing services.

### `MsixPackage` (sealed class, `IPackage`)

The primary entry point for reading a package from a file, stream, or loose
directory.

| Member | Description |
|--------|-------------|
| `static MsixPackage Open(string path)` | Open a `.msix`/`.appx`; throws `MsixPackageTypeException` for a bundle. |
| `static MsixPackage Open(Stream stream, bool leaveOpen = false)` | Open from a seekable stream. |
| `static MsixPackage OpenDirectory(string directory)` | Open an unpacked ("loose") layout. |
| `static bool IsBundle(string path)` | Detect an `.msixbundle`/`.appxbundle` container. |
| `IOpcPackage Opc { get; }` | The underlying OPC/ZIP container. |
| `AppxManifest Manifest { get; }` | Parsed `AppxManifest.xml` (lazy). |
| `BlockMap BlockMap { get; }` | Parsed `AppxBlockMap.xml` (lazy). |
| `PackageIdentity Identity { get; }` | Package identity (from the manifest). |
| `string DisplayName / PublisherDisplayName { get; }` | Display metadata. |
| `IReadOnlyList<string> Capabilities { get; }` | Declared capabilities. |
| `bool IsSigned { get; }` | Whether an `AppxSignature.p7x` part is present. |
| `BlockMapVerificationResult VerifyBlockMap()` | Verify payload against the block map. |
| `PackageSignature? ReadSignature()` | Read signer identity/CMS status, or `null` if unsigned. |
| `ManifestValidationResult ValidateManifest()` | Validate the manifest's semantics (see [manifest validation](manifest-validation.md)). |
| `Stream? OpenLogo()` | Open the package logo, or `null`. |
| `void Dispose()` | Release the underlying container. |

### `MsixBundle` (sealed class)

Explicit bundle reader: `Open(string)` / `Open(Stream, bool)`, `Opc`, `Manifest`,
`Identity`, `Packages`, and `Dispose()`.

### `IPackage` (interface, `IDisposable`)

Read-only view over package metadata (`Identity`, `DisplayName`,
`PublisherDisplayName`, `Capabilities`, `OpenLogo()`).

### `PackageIdentity`

Immutable `<Identity>` model.

| Member | Description |
|--------|-------------|
| `string Name / Publisher { get; }` | Required identity fields. |
| `Version Version { get; }` | Four-part version. |
| `ProcessorArchitecture Architecture { get; }` | Declared architecture (default `Neutral`). |
| `string ResourceId { get; }` | Resource id (empty for the main package). |
| `string PackageFamilyName { get; }` | `{Name}_{publisherHash}`. |
| `string PackageFullName { get; }` | `{Name}_{Version}_{Arch}_{ResourceId}_{publisherHash}`. |
| `static string ArchitectureMoniker(ProcessorArchitecture)` | Lowercase moniker (`x64`, `neutral`, ...). |

### `PublisherHash` (static class)

`static string Compute(string publisher)` — the 13-character MSIX Base32
publisher hash used in family/full names.

### `ProcessorArchitecture` (enum)

`X86=0`, `Arm=5`, `X64=9`, `Neutral=11`, `Arm64=12`, `X86OnArm64=14`,
`Unknown=0xFFFF` (numeric values match Windows `APPX_PACKAGE_ARCHITECTURE`).

### OPC — `MsixCore.Packaging.Opc`

| Type | Kind | Key members |
|------|------|-------------|
| `IOpcPackage` | interface, `IDisposable` | `PartNames`, required snapshot-consistency check `DetectSnapshotDrift()`, `ContainsPart(name)`, `OpenPart(name)`. Decorators must delegate the drift check rather than silently assume stability. |
| `OpcPackage` | sealed class | `static Open(string)`, `static Open(Stream, bool)`; ZIP-backed reader. |
| `DirectoryOpcPackage` | sealed class | `static Open(string directory)`, `RootDirectory`; loose-layout reader. |
| `OpcPartNames` | static class | Constants: `AppxManifest`, `AppxBundleManifest`, `AppxBlockMap`, `AppxSignature`, `ContentTypes`. |

### Manifest — `MsixCore.Packaging.Manifest`

| Type | Kind | Description |
|------|------|-------------|
| `AppxManifestParser` | static class | `Parse(Stream)` / `Parse(XDocument)` → `AppxManifest`. |
| `AppxManifest` | record | Identity, display/publisher names, description, logo, `IsFramework`, capabilities (flat `Capabilities` names plus categorized `DeclaredCapabilities`), applications, target device families, package dependencies, package-level extensions. |
| `ManifestApplication` | record | `Id`, `Executable`, `EntryPoint`, `VisualElements`, `Extensions`. |
| `VisualElements` | record | `DisplayName`, `Description`, `Square150x150Logo`, `Square44x44Logo`, `BackgroundColor`, `AppListEntry`, `VisualGroup`, `DefaultTile`, `SplashScreen`, `LockScreen`, `InitialRotationPreferences`. |
| `DefaultTile` | record | `Wide310x150Logo`, `Square310x310Logo`, `Square71x71Logo`, `ShortName`, `ShowNameOnTiles`. The wide/large/small logos live here, not on `VisualElements`. |
| `SplashScreen` | record | `Image`, `BackgroundColor`, `IsOptional` (`uap5:Optional`). |
| `LockScreen` | record | `BadgeLogo`, `Notification`. |
| `ManifestCapability` | record | `Name`, `Kind`, `Namespace`, `Devices`. |
| `CapabilityKind` | enum | `Unknown`, `General`, `Device`, `Restricted`, `Windows`, `Custom` — derived from the declaring namespace, which is where MSIX carries the distinction. |
| `CapabilityDevice` | record | `Id`, `Functions` — the `Device`/`Function` children of a `DeviceCapability`. |
| `TargetDeviceFamily` | record | `Name`, `MinVersion`, `MaxVersionTested`. |
| `PackageDependency` | record | `Kind`, `Name`, `Publisher`, `MinVersion`, `MaxMajorVersionTested`, `IsOptional`. See [manifest dependencies](manifest-dependencies.md). |
| `PackageDependencyKind` | enum | `Framework`, `MainPackage`, `HostRuntime`. |
| `AppExtension` | record | `Category`, `Executable`, `EntryPoint`, `StartPage`, `ResourceGroup`, `RuntimeType`, `Payload`. See [manifest extensions](manifest-extensions.md). |
| `ExtensionPayload` | abstract record | Base of the typed extension payloads below; `AppExtension.Payload` is `null` for an unrecognised category. |
| `FileTypeAssociationExtension` | record | `Name`, `DisplayName`, `Logo`, `InfoTip`, `FileTypes`. |
| `SupportedFileType` | record | `Extension` (leading dot preserved), `ContentType`. |
| `ProtocolExtension` | record | `Name`, `DisplayName`, `Logo`, `DesiredView`, `ReturnResults`, `Parameters`. |
| `AppExecutionAliasExtension` | record | `Aliases`. |
| `StartupTaskExtension` | record | `TaskId`, `IsEnabled` (nullable), `DisplayName`. |
| `FullTrustProcessExtension` | record | `ParameterGroups`. |
| `ParameterGroup` | record | `GroupId`, `Parameters`. |
| `ComServerExtension` | record | `ExeServers`, `SurrogateServers`, `ProgIds`. |
| `ComExeServer` / `ComSurrogateServer` / `ComClass` / `ComProgId` | records | COM registration detail. |
| `ShortcutExtension` | record | `File`, `Icon`, `Arguments`, `Description`, `PinToStartMenu` (nullable). |
| `BundleManifestParser` | static class | `Parse(...)` → `BundleManifest`. |
| `BundleManifest` | record | `Identity`, `Packages`. |
| `BundlePackageEntry` | record | `FileName`, `Type`, `Version`, `Architecture`, `ResourceId`, `Resources`. |
| `BundleResource` | record | `Language`, `Scale`, `DXFeatureLevel`. |
| `BundlePackageType` | enum | `Application`, `Resource`. |

### Integrity — `MsixCore.Packaging.Integrity`

| Type | Kind | Description |
|------|------|-------------|
| `BlockMapParser` | static class | `Parse(Stream)` / `Parse(XDocument)` → `BlockMap`. |
| `BlockMap` | record | `HashMethod`, `Files`; `const int BlockSize = 65536`. |
| `BlockMapFile` | record | `Name`, `Size`, `Blocks`. |
| `BlockMapBlock` | record | `Hash` (base64), `CompressedSize?`. |
| `BlockMapHashMethod` | enum | `Sha256`, `Sha384`, `Sha512`. |
| `BlockMapVerifier` | static class | `Verify(...)`, `VerifyCoverage(...)`, and `VerifyAndCopy(...)` for one-pass extraction validation. |
| `BlockMapVerificationResult` | record | `IsValid`, `Files`, `CoverageErrors`. |
| `BlockMapFileResult` | record | `Name`, `IsValid`, `Error?`. |
| `PackageSignatureReader` | static class | `Read(Stream)` / `Read(byte[])` → `PackageSignature`. |
| `PackageSignature` | record | `SubjectName`, `SubjectNameRawData` (raw DER subject bytes), `IssuerName`, `Thumbprint`, `NotBefore/NotAfter`, `IsCmsIntegrityValid`; `bool MatchesPublisher(string)` compares by decoded RDN sequence. |

### Validation — `MsixCore.Packaging.Validation`

Semantic manifest validation, opt-in and separate from parsing. See
[manifest validation](manifest-validation.md) for the rule list and known
divergences from Windows.

| Type | Kind | Description |
|------|------|-------------|
| `ManifestValidator` | static class | `Validate(AppxManifest)`, `Validate(AppxManifest, XDocument)` (adds the namespace check), `Validate(Stream)` (parses and validates). |
| `ManifestValidationResult` | sealed class | `Issues`, `IsValid` (no errors), `Errors`, `Warnings`. |
| `ManifestValidationIssue` | record | `Severity`, `Rule`, `Target`, `Message`. |
| `ManifestValidationSeverity` | enum | `Warning`, `Error`. |
| `ManifestValidationRule` | enum | Stable rule identifiers, e.g. `IdentifierReserved`, `VersionRangeInverted`, `UnknownNamespace`. |
| `ManifestNamespaces` | static class | `Package` / `Bundle` namespace→schema tables; `IsKnownPackageNamespace(string)`, `IsKnownBundleNamespace(string)`. |

## `MsixCore.PackageStore`

Extraction and the decision logic a deployment tool needs. This assembly does
**not** install packages — see
[architecture](architecture.md#why-there-is-no-install-engine) for why the
staging/commit engine was removed.

### `PackageExtractor` (static class)

Cross-platform extraction of an OPC package to a loose directory. Powers the
`unpack` verb.

| Member | Description |
|--------|-------------|
| `static void Extract(IOpcPackage package, string destination, IProgress<float>? progress = null, CancellationToken = default)` | Extract all parts under `destination`, reporting 0–100 progress. Throws `InvalidDataException` if a part escapes the destination or a symlink/junction (incl. dangling, or the root itself) is on the path. It performs **no integrity verification**; do not trust later extraction from a loose directory based on an earlier validation. |
| `static BlockMapVerificationResult ExtractAndVerify(IOpcPackage package, BlockMap blockMap, string destination, ...)` | Preferred path for trusted extraction: extract and hash each payload in the same read, then require `IsValid` before using the output. |

### `InstalledPackageInfo` (sealed record)

Metadata of a package already installed somewhere, owning no file handles. This
is the input to `DependencyResolver`.

| Member | Description |
|--------|-------------|
| `Identity`, `DisplayName`, `PublisherDisplayName`, `Capabilities`, `IsFramework`, `LogoPath`, `ExecutablePath` | Manifest metadata. |
| `string InstalledLocation` | Absolute package root. |
| `static InstalledPackageInfo ReadFromDirectory(string directory)` | Read only the manifest of an installed layout. A directory or reparse point named `AppxManifest.xml` is rejected rather than reported as absent. |
| `MsixPackage OpenPackage()` | Open the installed content on demand. |

### `DependencyResolver` (static class)

Resolves a manifest's declared `Dependencies` against a set of installed
packages. The installed set is supplied by the caller rather than read from a
store, because the authority on what is installed differs by host: on Windows it
is the OS deployment stack, in CI a directory of staged packages, in a
deployment tool that tool's own inventory. The decision itself is the same in
every case.

| Member | Description |
|--------|-------------|
| `DependencyResolutionResult Resolve(AppxManifest manifest, IEnumerable<InstalledPackageInfo> installedPackages)` | One `DependencyResolution` per declared dependency, in manifest order. The sequence is materialised once, and is not enumerated at all when the manifest declares no dependencies. |
| `DependencyResolutionResult` | `Resolutions`, `IsSatisfied`, `Unsatisfied`, `Blocking`, `CanDeploy`. `CanDeploy` excludes `uap6:Optional` dependencies, so it — not `IsSatisfied` — is the gate a deployment tool should use. |
| `DependencyResolution` | `Dependency`, `Status`, `ResolvedPackage`, `IsSatisfied`, `Describe()`. |
| `DependencyResolutionStatus` | `Resolved`, `NotInstalled`, `NotAFramework`, `VersionTooLow`. |

See [manifest dependencies](manifest-dependencies.md) for the resolution rules and divergences.

