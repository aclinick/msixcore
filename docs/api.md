# Public API reference

A concise map of the public surface of `MsixCore.Packaging` and
`MsixCore.Deployment`. Signatures are abbreviated; see the XML doc comments in
source for full details. Types are immutable `record`s unless noted.

## `MsixCore.Packaging`

### `MsixPackage` (sealed class, `IPackage`)

The primary entry point for reading a package from a file, stream, or loose
directory.

| Member | Description |
|--------|-------------|
| `static MsixPackage Open(string path)` | Open a `.msix`/`.appx` (or bundle) file. |
| `static MsixPackage Open(Stream stream, bool leaveOpen = false)` | Open from a seekable stream. |
| `static MsixPackage OpenDirectory(string directory)` | Open an unpacked ("loose") layout. |
| `IOpcPackage Opc { get; }` | The underlying OPC/ZIP container. |
| `AppxManifest Manifest { get; }` | Parsed `AppxManifest.xml` (lazy). |
| `BlockMap BlockMap { get; }` | Parsed `AppxBlockMap.xml` (lazy). |
| `PackageIdentity Identity { get; }` | Package identity (from the manifest). |
| `string DisplayName / PublisherDisplayName { get; }` | Display metadata. |
| `IReadOnlyList<string> Capabilities { get; }` | Declared capabilities. |
| `bool IsSigned { get; }` | Whether an `AppxSignature.p7x` part is present. |
| `BlockMapVerificationResult VerifyBlockMap()` | Verify payload against the block map. |
| `PackageSignature? ReadSignature()` | Read signer identity/CMS status, or `null` if unsigned. |
| `Stream? OpenLogo()` | Open the package logo, or `null`. |
| `void Dispose()` | Release the underlying container. |

### `IPackage` (interface, `IDisposable`)

Read-only view over package metadata (`Identity`, `DisplayName`,
`PublisherDisplayName`, `Capabilities`, `OpenLogo()`).

### `IInstalledPackage : IPackage`

Adds `string InstalledLocation` and `ExecutionInfo? ExecutionInfo`.

### `ExecutionInfo`

Resolved launch info: `ResolvedExecutableFilePath`, `CommandLineArguments`,
`WorkingDirectory`.

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
| `IOpcPackage` | interface, `IDisposable` | `PartNames`, `ContainsPart(name)`, `OpenPart(name)`. |
| `OpcPackage` | sealed class | `static Open(string)`, `static Open(Stream, bool)`; ZIP-backed reader. |
| `DirectoryOpcPackage` | sealed class | `static Open(string directory)`, `RootDirectory`; loose-layout reader. |
| `OpcPartNames` | static class | Constants: `AppxManifest`, `AppxBundleManifest`, `AppxBlockMap`, `AppxSignature`, `ContentTypes`. |

### Manifest — `MsixCore.Packaging.Manifest`

| Type | Kind | Description |
|------|------|-------------|
| `AppxManifestParser` | static class | `Parse(Stream)` / `Parse(XDocument)` → `AppxManifest`. |
| `AppxManifest` | record | Identity, display/publisher names, description, logo, `IsFramework`, capabilities, applications, target device families. |
| `ManifestApplication` | record | `Id`, `Executable`, `EntryPoint`, `VisualElements`. |
| `VisualElements` | record | `DisplayName`, `Description`, logos, `BackgroundColor`, `AppListEntry`. |
| `TargetDeviceFamily` | record | `Name`, `MinVersion`, `MaxVersionTested`. |
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
| `BlockMapVerifier` | static class | `Verify(IOpcPackage, BlockMap)` → `BlockMapVerificationResult`. |
| `BlockMapVerificationResult` | record | `IsValid`, `Files`, `CoverageErrors`. |
| `BlockMapFileResult` | record | `Name`, `IsValid`, `Error?`. |
| `PackageSignatureReader` | static class | `Read(Stream)` / `Read(byte[])` → `PackageSignature`. |
| `PackageSignature` | record | `SubjectName`, `SubjectNameRawData` (raw DER subject bytes), `IssuerName`, `Thumbprint`, `NotBefore/NotAfter`, `IsCmsIntegrityValid`; `bool MatchesPublisher(string)` compares by decoded RDN sequence. |

## `MsixCore.Deployment`

### `IPackageManager` / `PackageManager` (sealed class)

Lifecycle + query over an `IPackageStore`.

| Member | Description | Status |
|--------|-------------|--------|
| `IMsixResponse AddPackage(string path, DeploymentOptions = None, CancellationToken = default)` | Install a package: verify block map, extract to staging, transactionally commit. Returns immediately; runs on a background task. | Implemented. |
| `IMsixResponse RemovePackage(string fullName, CancellationToken = default)` | Uninstall by full name. Returns immediately. | Implemented. |
| `IInstalledPackage? FindPackage(string fullName)` | Find one by full name. | Implemented. |
| `IInstalledPackage? FindPackageByFamilyName(string familyName)` | Find one by family name. | Implemented. |
| `IReadOnlyList<IInstalledPackage> FindPackages(string pattern)` | Glob query (`*`, `?`). | Implemented. |
| `IPackage GetMsixPackageInfo(string msixFilePath)` | Read metadata without installing. | Implemented. |

`PackageManager` has a default constructor (backed by
`FileSystemPackageStore.CreateDefault()`) and a `PackageManager(IPackageStore)`
constructor.

### `PackageExtractor` (static class)

Cross-platform extraction of an OPC package to a loose directory. Powers the
`unpack` verb and the install engine.

| Member | Description |
|--------|-------------|
| `static void Extract(IOpcPackage package, string destination, IProgress<float>? progress = null, CancellationToken = default)` | Extract all parts under `destination`, reporting 0–100 progress. Throws `InvalidDataException` if a part escapes the destination or a symlink/junction (incl. dangling, or the root itself) is on the path. |

### `IPackageStore` / `FileSystemPackageStore` (sealed class)

`IPackageStore` is now writable/transactional; `FileSystemPackageStore`
implements it over a store root.

| Member | Description |
|--------|-------------|
| `IReadOnlyList<IInstalledPackage> EnumeratePackages()` | Enumerate installed packages (caller disposes each). Skips `.`-prefixed internal dirs. |
| `string GetInstallLocation(string packageFullName)` | The (possibly not-yet-existing) install directory for a package. |
| `bool Contains(string packageFullName)` | Whether a package is currently installed. |
| `void Delete(string packageFullName)` | Remove an installed package's payload (no-op if absent). |
| `string CreateStagingLocation()` | Create a fresh empty staging directory to extract into. |
| `void Commit(string stagingLocation, string packageFullName)` | Transactionally promote staging to the install location: move existing aside, promote, roll back on failure; serialized per destination. |
| `FileSystemPackageStore(string rootDirectory)` | Store rooted at a directory. |
| `static FileSystemPackageStore CreateDefault()` | Store under `LocalApplicationData/MsixCore/Packages`. |
| `string RootDirectory { get; }` | Absolute store root. |
| `const string DefaultStoreFolderName` | `"MsixCore/Packages"`. |

### `InstalledPackage` (sealed class, `IInstalledPackage`)

`static InstalledPackage OpenDirectory(string directory)`; exposes
`InstalledLocation`, `Identity`, `DisplayName`, `PublisherDisplayName`,
`Capabilities`, `ExecutionInfo`, `OpenLogo()`, `Dispose()`.

### `IMsixResponse` (interface)

Async operation surface: `float Percentage`, `InstallationStep Status`,
`string StatusText`, `Exception? Failure`, `Task Completion`,
`event EventHandler<IMsixResponse>? ProgressChanged`, `void Cancel()`. Returned
by `AddPackage`/`RemovePackage`. The concrete driver (`MsixResponse`) and the
inline `SynchronousProgress<T>` reporter are `internal` implementation details.

### `InstallationStep` (enum)

`Unknown`, `Started`, `GetPackageInformation`, `Extraction`, `Integration`,
`Completed`, `Error` — coarse progress stages (ordered by deployment sequence).

### `DeploymentOptions` (flags enum)

`None = 0`, `ForceApplicationShutdown = 1`, `ExtractOnly = 2`.

### Handlers — `MsixCore.Deployment.Handlers`

| Type | Kind | Description |
|------|------|-------------|
| `IPackageHandler` | interface | A pipeline step: `Name`, `IsApplicable(context)`, `ExecuteAddAsync(...)`, `ExecuteRemoveAsync(...)`. |
| `PackageDeploymentContext` | sealed class | `Package`, `InstallLocation`, `Options`. |
