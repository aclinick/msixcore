# MSIX format-spec coverage matrix

This document catalogs, feature by feature, what the `aclinick/msixcore` C#/.NET 10 port of
Microsoft MSIX Core supports **today**, versus what "modern MSIX" defines. It is derived by reading
the implementation under `src/` at the current tip of `main` and cross-referencing the official
MSIX/APPX format and manifest-schema documentation.

- **Scope of the port** (per `README.md`): a cross-platform reader/validator and unsigned
  authoring layer (`MsixCore.Packaging`), a deployment/query layer (`MsixCore.Deployment`),
  and the `msixmgr` CLI. Signature production is an explicit non-goal: packages are packed
  unsigned, signed by external SignTool/CI signing services, then validated here.
- **Verification method**: every "Supported" claim below points at the concrete file that implements
  it. Where a feature is only partially present, the gap is called out. Nothing is marked supported
  on the strength of a type/interface stub alone (those are marked *partial* or *no*).

Legend: **yes** = implemented and exercised; **partial** = present but incomplete or not wired into
the public read path; **no** = not implemented.

> **Refreshed after merging `origin/main` (Phase 5/6 deployment engine).** The port now has a working
> cross-platform **install engine** (extract → stage → commit with rollback), a `PackageExtractor`,
> the `msixmgr unpack` verb, and the publisher-DN matching bug ([#12](https://github.com/aclinick/msixcore/issues/12))
> is **fixed**. Counts below reflect the merged state.

## Summary counts

| Area | yes | partial | no | rows |
| --- | --- | --- | --- | --- |
| Container / OPC | 4 | 2 | 2 | 8 |
| Manifest (AppxManifest.xml) | 7 | 2 | 7 | 16 |
| Extensions | 0 | 0 | 11 | 11 |
| Block map | 5 | 1 | 1 | 7 |
| Signature | 4 | 0 | 5 | 9 |
| Bundles | 1 | 1 | 4 | 6 |
| Package kinds | 1 | 2 | 4 | 7 |
| Deployment | 9 | 2 | 3 | 14 |
| **Total** | **31** | **10** | **37** | **78** |

**Headline:** the port is now a security-conscious **single-package reader/validator _and_ installer**:
OPC (with percent-decoding + footprint handling) + manifest identity/properties + block map +
CMS-envelope signature reading + faithful publisher-DN matching, plus a transactional
extract/stage/commit/rollback install/uninstall engine and loose-layout extraction. It still does
**not** cover: manifest extensions, most manifest namespaces beyond identity/properties, framework/
dependency resolution, bundle detection/resolution, non-main package kinds, deep signature
binding/trust, or OS-integration handlers (shortcuts, ARP, file-type/protocol registration).

Key doc references (full list per row): MSIX package format overview
<https://learn.microsoft.com/en-us/windows/msix/overview>; package manifest schema reference
<https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/schema-root>; bundle
manifest schema <https://learn.microsoft.com/en-us/uwp/schemas/bundlemanifestschema/root-elements-bundle-manifest>;
signing overview <https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview>.

---

## 1. Container / OPC (Open Packaging Conventions ZIP)

| Feature | MSIX spec reference | Supported? | Where (file) | Notes / gaps |
| --- | --- | --- | --- | --- |
| Open OPC ZIP container from file/stream | [MSIX overview – package is a ZIP/OPC container](https://learn.microsoft.com/en-us/windows/msix/overview) | yes | `MsixCore.Packaging/Opc/OpcPackage.cs` | Backed by `System.IO.Compression.ZipArchive`; read-only, cross-platform. |
| Loose / unpacked directory layout | [MSIX overview](https://learn.microsoft.com/en-us/windows/msix/overview) | yes | `MsixCore.Packaging/Opc/DirectoryOpcPackage.cs` | Reads an unpacked package dir; skips reparse points; enforces root containment. |
| OPC part-name rules (no rooting, no `..`, no dup, case-insensitive equivalence) | [OPC / MSIX overview](https://learn.microsoft.com/en-us/windows/msix/overview) | yes | `OpcPackage.IsValidPartName`, `OpcPackage` ctor | Also a zip-slip defense; duplicate/equivalent part names rejected. |
| Percent-encoded part-name canonicalization (`%21`→`!`, reject encoded separators/control chars) | [OPC physical model](https://learn.microsoft.com/en-us/windows/msix/overview) | yes | `OpcPackage.TryCanonicalizePartName` | Decodes each segment to the logical name used by the block map/manifest; re-validates after decode to defeat `%2e%2e`→`..` traversal. |
| Zip64 / large packages | [MSIX overview](https://learn.microsoft.com/en-us/windows/msix/overview) | partial | `OpcPackage.cs` | Inherited from `ZipArchive` (which supports Zip64); not explicitly tested by the port. |
| `[Content_Types].xml` parsing / content-type validation | [OPC content types](https://learn.microsoft.com/en-us/windows/msix/overview) | no | `OpcPartNames.ContentTypes` (constant only) | The part name is known and excluded from block-map coverage, but the content-types map is never parsed or validated against payload parts. |
| `AppxMetadata/` footprint parts (e.g. `CodeIntegrity.cat`) | [Signing overview](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview) | partial | `OpcPartNames.CodeIntegrityCatalog`, `BlockMapVerifier.ExcludedParts` | Recognized as a footprint part and excluded from block-map coverage, but the catalog is not parsed or verified. |
| Encrypted packages (`.eappx`/`.emsix`) | [Package encryption](https://learn.microsoft.com/en-us/windows/msix/overview) | no | — | Not supported. |

---

## 2. Manifest — `AppxManifest.xml`

Parser: `MsixCore.Packaging/Manifest/AppxManifestParser.cs` (namespace-tolerant: elements matched by
local name; XXE-hardened via `DtdProcessing.Prohibit` + null resolver). Root schema:
<https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-f-package>.

| Feature | MSIX spec reference | Supported? | Where (file) | Notes / gaps |
| --- | --- | --- | --- | --- |
| `Package` root + namespace-tolerant parse | [Package element](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-f-package) | yes | `AppxManifestParser.Parse` | Matches by local name, so any schema revision parses. |
| `Identity` (Name, Publisher, Version, ProcessorArchitecture, ResourceId) | [Identity element](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-f-identity) | yes | `AppxManifestParser.ParseIdentity`, `PackageIdentity.cs` | Required attrs enforced. |
| Version quad validation (four parts, each 0–65535) | [ST_VersionQuad](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-f-identity) | yes | `Manifest/ManifestVersion.cs` | Stricter than `System.Version`. |
| `PackageFamilyName` / `PackageFullName` + publisher hash | [Package identity](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/package-identity) | yes | `PackageIdentity.cs`, `PublisherHash.cs` | Base32 publisher hash verified against the canonical `8wekyb3d8bbwe` case. |
| `Properties`: DisplayName, PublisherDisplayName, Description, Logo, Framework | [Properties element](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-f-properties) | yes | `AppxManifestParser.Parse`, `AppxManifest.cs` | Framework flag parsed with XML boolean semantics. |
| `Properties`: other (SupportedUsers, ModificationPackage, AutoUpdate, etc.) | [Properties element](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-f-properties) | no | — | Not read; matters for optional/modification/self-updating packages. |
| `Capabilities` (capability names) | [Capabilities element](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-f-capabilities) | partial | `AppxManifestParser.ParseCapabilities` | Collects `Name` of every capability-like child but does **not** distinguish `Capability` / `DeviceCapability` / `rescap:Capability` / `uap:Capability` / `CustomCapability`, nor validate them. |
| `Applications/Application` (Id, Executable, EntryPoint) | [Application element](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-f-application) | yes | `AppxManifestParser.ParseApplications`, `ManifestApplication.cs` | Id required; Executable/EntryPoint optional. |
| `uap:VisualElements` (DisplayName, Description, 150/44 logos, BackgroundColor, AppListEntry) | [VisualElements](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap-visualelements) | partial | `AppxManifestParser.ParseVisualElements`, `VisualElements.cs` | Missing: wide/large/small logos, `DefaultTile`, `SplashScreen`, `LockScreen`, `InitialRotationPreference`, `ShowNameOnTiles`, etc. |
| `Application` modern attrs (uap10:TrustLevel, uap10:RuntimeBehavior, HostId/HostRuntime) | [uap10 hostRuntime](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap10-hostruntime) | no | — | Needed for containerized / host-runtime apps. |
| `Dependencies/TargetDeviceFamily` (Name, MinVersion, MaxVersionTested) | [TargetDeviceFamily](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-f-targetdevicefamily) | yes | `AppxManifestParser.ParseTargetDeviceFamilies`, `TargetDeviceFamily.cs` | All three attributes parsed. |
| `Dependencies/PackageDependency` (framework refs: Name, MinVersion, Publisher) | [PackageDependency](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-f-packagedependency) | no | — | Framework dependency graph not modeled; blocks framework resolution at install. |
| `Dependencies/uap4:MainPackageDependency` (optional/modification) | [MainPackageDependency](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap4-mainpackagedependency) | no | — | Needed to relate optional/modification packages to their main package. |
| `Dependencies/uap10:HostRuntimeDependency` | [HostRuntimeDependency](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap10-hostruntimedependency) | no | — | Host-runtime packages not modeled. |
| `Resources/Resource` (language/scale/DXFL of the package itself) | [Resources element](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-f-resources) | no | — | Package-level resource qualifiers not read (bundle-level qualifiers *are*, see §6). |
| Build metadata (`build:Metadata`/`metadata:Item`) | [Package manifest schema](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/schema-root) | no | — | Toolchain provenance not surfaced. |

---

## 3. Extensions (`Application/Extensions` and package-level `Extensions`)

None of the modern MSIX extension categories are parsed. Extension category overview:
<https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions>.

| Feature | MSIX spec reference | Supported? | Where (file) | Notes / gaps |
| --- | --- | --- | --- | --- |
| File type associations (`uap:FileTypeAssociation`) | [FileTypeAssociation](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap-filetypeassociation) | no | — | Required for shell file registration. |
| Protocols / URI schemes (`uap:Protocol`, `windows.protocol`) | [uap:Protocol](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap-protocol) | no | — | The `ManifestApplication` XML doc even mentions protocol but no parsing exists. |
| App execution aliases (`uap5:AppExecutionAlias`) | [AppExecutionAlias](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap5-appexecutionalias) | no | — | PATH alias registration. |
| Startup tasks (`desktop:Extension` `windows.startupTask`) | [startupTask extension](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop-extension) | no | — | — |
| App services (`uap:Extension` `windows.appService`) | [appService](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap-appservice) | no | — | — |
| Background tasks (`windows.backgroundTasks`) | [BackgroundTasks](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-f-backgroundtasks) | no | — | — |
| Share target (`uap:ShareTarget`) | [ShareTarget](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap-sharetarget) | no | — | — |
| COM/OLE servers (`com:Extension`, `windows.comServer`) | [com:Extension](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-com-extension) | no | — | Out-of-process / in-process COM registration. |
| Full-trust process & shortcuts (`desktop:Extension`) | [desktop:Extension](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop-extension) | no | — | `windows.fullTrustProcess`, shortcut extensions. |
| File Explorer context menus (`desktop4/5:Extension`) | [desktop4:Extension](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop4-extension) | no | — | Sparse/modern context menu handlers. |
| App extension host/guest (`uap3:AppExtension`, `uap3:AppExtensionHost`) | [AppExtension](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap3-appextension) | no | — | Add-in model. |

---

## 4. Block map — `AppxBlockMap.xml`

Parser `MsixCore.Packaging/Integrity/BlockMapParser.cs`; verifier `.../BlockMapVerifier.cs`.
Reference: [MSIX overview – AppxBlockMap.xml](https://learn.microsoft.com/en-us/windows/msix/overview).

| Feature | MSIX spec reference | Supported? | Where (file) | Notes / gaps |
| --- | --- | --- | --- | --- |
| Parse `BlockMap` (HashMethod, Files, Blocks) | [AppxBlockMap.xml](https://learn.microsoft.com/en-us/windows/msix/overview) | yes | `BlockMapParser.cs`, `BlockMap.cs` | Backslashes normalized to `/` to match part names. |
| HashMethod SHA-256/384/512 | [Block map hash](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview) | yes | `BlockMapParser.ParseHashMethod` | Missing/unknown method rejected. |
| Per-block uncompressed hash verification (64 KiB blocks) | [Package integrity enforcement](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview) | yes | `BlockMapVerifier.VerifyContent` | Streams uncompressed content via `IncrementalHash`-equivalent per-block hashing. |
| Total-size verification per file | [AppxBlockMap.xml](https://learn.microsoft.com/en-us/windows/msix/overview) | yes | `BlockMapVerifier.VerifyContent` | Rejects short/long files. |
| Coverage: payload parts ↔ block-map files (both directions) | [Package integrity](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview) | yes | `BlockMapVerifier.CheckCoverage` | Excludes footprint parts `[Content_Types].xml`, `AppxBlockMap.xml`, `AppxSignature.p7x`, and `AppxMetadata/CodeIntegrity.cat`. |
| Per-block compressed `Size` (LFH stored size) validation | [AppxBlockMap.xml](https://learn.microsoft.com/en-us/windows/msix/overview) | partial | `BlockMapParser` (parses `Size`) | Compressed size is parsed into `BlockMapBlock.CompressedSize` but never enforced against the ZIP local file header. |
| Local file header / offset binding used by MSIX signing | [Signing overview](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview) | no | — | Not validated (relevant to the AXBM digest, see §5). |

---

## 5. Signature — `AppxSignature.p7x`

Reader `MsixCore.Packaging/Integrity/PackageSignatureReader.cs`; model `PackageSignature.cs`.
Reference: [Sign an MSIX package](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview).

MSIX Core's signature scope is read/verify only. Signature production is an intentional
non-goal delegated to Windows SignTool/signcode and CI/CD signing services (for example,
Azure Trusted Signing / Artifact Signing, DigiCert KeyLocker, SSL.com eSigner, and
SignPath). The expected toolchain is pack here, sign externally, then validate here. Rows marked **no** for APPX
indirect-data binding, trust-chain evaluation, timestamp validation, and multiple signers
reflect this read-only, delegate-trust-to-platform stance, not a promise to implement
signing or platform trust policy.

| Feature | MSIX spec reference | Supported? | Where (file) | Notes / gaps |
| --- | --- | --- | --- | --- |
| Read `.p7x`, strip `PKCX` magic | [AppxSignature.p7x](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview) | yes | `PackageSignatureReader.StripMagic` | Rejects a file missing the 4-byte identifier. |
| Decode CMS / extract primary signer certificate | [Signing overview](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview) | yes | `PackageSignatureReader.Read` | Uses `SignedCms` (OpenSSL-backed on Linux); subject/issuer/thumbprint/validity surfaced. |
| CMS envelope integrity (`CheckSignature(verifySignatureOnly:true)`) | [Package integrity enforcement](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview) | yes | `PackageSignatureReader.Read` | Asserts the CMS digest/signature are internally consistent — **not** authenticity. |
| Publisher (`Identity/@Publisher`) equals signer subject DN | [Publisher = signing subject](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview) | yes | `PackageSignature.MatchesPublisher` (+ `SubjectNameRawData`) | Fixed ([#12](https://github.com/aclinick/msixcore/issues/12)): decodes RDNs from the certificate's original subject DER and compares attribute type + decoded value, so it is faithful to ASN.1 string encoding (`PrintableString` vs `UTF8String`) and RDN order — no more false mismatches. |
| APPX indirect-data digest binding (AXPC/AXCT/AXBM/AXCI/AXCF SIP headers) | [Package integrity enforcement](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview) | no | — | **Explicitly not implemented** (documented in `PackageSignature` / `ValidateCommand`). The signature is not verified to actually bind the block map/content — the core authenticity gap. |
| Certificate trust-chain evaluation | [Signing overview](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview) | no | — | Intentionally separate; no chain/root policy. |
| Timestamp countersignature validation | [Timestamping](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview) | no | — | Not read. |
| Multiple signers | [Signing overview](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview) | no | `SignerInfos[0]` only | Only the first signer is examined. |
| Catalog signature (`AppxMetadata/CodeIntegrity.cat`) | [Signing overview](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview) | no | — | Not handled. |

---

## 6. Bundles — `AppxBundleManifest.xml`

Parser `MsixCore.Packaging/Manifest/BundleManifestParser.cs`; model `BundleManifest.cs`.
Reference: [Bundle manifest schema](https://learn.microsoft.com/en-us/uwp/schemas/bundlemanifestschema/root-elements-bundle-manifest).

| Feature | MSIX spec reference | Supported? | Where (file) | Notes / gaps |
| --- | --- | --- | --- | --- |
| Parse `Bundle` manifest (Identity + Packages) | [Bundle manifest root](https://learn.microsoft.com/en-us/uwp/schemas/bundlemanifestschema/root-elements-bundle-manifest) | yes | `MsixBundle.cs`, `BundleManifestParser.cs` | Bundles have an explicit reader; opening one as `MsixPackage` throws `MsixPackageTypeException`. |
| Package entry (FileName, Type, Version, Architecture, ResourceId, Resource qualifiers) | [b:Package](https://learn.microsoft.com/en-us/uwp/schemas/bundlemanifestschema/element-package) | yes | `BundleManifestParser.ParsePackages`, `BundleManifest.cs` | Type defaults to `resource`; language/scale/DXFL qualifiers parsed. |
| Bundle detection + open `.msixbundle`/`.appxbundle` and enumerate children | [MSIX bundles](https://learn.microsoft.com/en-us/windows/msix/overview) | yes | `MsixPackage.IsBundle`, `MsixBundle.Open` | Child entries are exposed from the bundle manifest. |
| Bundle applicability (pick applicable app + resource packages by arch/language/scale/DXFL) | [Resource packages](https://learn.microsoft.com/en-us/windows/uwp/app-resources/resource-management-system) | no | — | No applicability engine. |
| `OptionalBundle` | [Bundle manifest schema](https://learn.microsoft.com/en-us/uwp/schemas/bundlemanifestschema/root-elements-bundle-manifest) | no | — | Related-set bundles not modeled. |
| Bundle block map / bundle signature | [Signing overview](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview) | no | — | A bundle's own `AppxBlockMap.xml`/`AppxSignature.p7x` over child packages isn't verified. |

---

## 7. Package kinds

Reference: [MSIX package types / related sets](https://learn.microsoft.com/en-us/windows/msix/package/optional-packages).

| Feature | MSIX spec reference | Supported? | Where (file) | Notes / gaps |
| --- | --- | --- | --- | --- |
| Main / application package | [MSIX overview](https://learn.microsoft.com/en-us/windows/msix/overview) | yes | `MsixPackage.cs` | The primary supported case. |
| Framework package | [Framework packages](https://learn.microsoft.com/en-us/windows/msix/framework-packages/framework-packages-overview) | partial | `AppxManifest.IsFramework` | Flag detected, but there is no framework resolution, sharing, or dependency install. |
| Resource package | [Resource packages](https://learn.microsoft.com/en-us/windows/uwp/app-resources/resource-management-system) | partial | `PackageIdentity.ResourceId` | Identity `ResourceId` parsed; no resource-package role handling or applicability. |
| Optional package | [Optional packages](https://learn.microsoft.com/en-us/windows/msix/package/optional-packages) | no | — | No `MainPackageDependency`, no related-set handling. |
| Modification package | [Modification packages](https://learn.microsoft.com/en-us/windows/msix/modification-package-authoring/modification-package) | no | — | Not modeled. |
| Host runtime package | [uap10:HostRuntime](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap10-hostruntime) | no | — | Not modeled. |
| Sparse / external-location package | [Grant identity to non-packaged apps (sparse)](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps) | no | — | `AllowExternalContent` / external location not handled. |

---

## 8. Deployment (`MsixCore.Deployment`)

| Feature | MSIX spec reference | Supported? | Where (file) | Notes / gaps |
| --- | --- | --- | --- | --- |
| Query: FindPackage / FindPackageByFamilyName / FindPackages (wildcard) | [PackageManager API parity](https://learn.microsoft.com/en-us/uwp/api/windows.management.deployment.packagemanager) | yes | `Deployment/PackageManager.cs` | Metadata-first; exact full-name lookup is targeted and family lookup is deterministic. |
| Get package info from a file | [PackageManager API](https://learn.microsoft.com/en-us/uwp/api/windows.management.deployment.packagemanager) | yes | `PackageManager.GetMsixPackageInfo` | Opens an `MsixPackage`. |
| Enumerate installed (loose) packages | — | yes | `Deployment/FileSystemPackageStore.cs` | Treats any non-reserved subdir with `AppxManifest.xml` as installed; skips `.`-prefixed staging/backup dirs. |
| Resolve entry-point execution info | [MSIX overview](https://learn.microsoft.com/en-us/windows/msix/overview) | yes | `Deployment/InstalledPackage.cs` | Resolves first app with an `Executable`; guards against path traversal. |
| Loose extraction (unpack) with traversal + symlink/junction containment | [MSIX overview](https://learn.microsoft.com/en-us/windows/msix/overview) | yes | `Deployment/PackageExtractor.cs` | Pure managed; rejects reparse-point escapes (including a dangling link and a reparse-point root) and out-of-root part paths; cooperative cancellation. |
| Add / install (extract → stage → commit) | [Managing MSIX deployment](https://learn.microsoft.com/en-us/windows/msix/desktop/managing-your-msix-deployment-overview) | yes | `PackageManager.RunAdd`, `PackageExtractor.ExtractAndVerify`, `FileSystemPackageStore.Commit` | Hashes while extracting to `.staging` and commits only validated content. |
| Remove / uninstall | [Managing MSIX deployment](https://learn.microsoft.com/en-us/windows/msix/desktop/managing-your-msix-deployment-overview) | yes | `PackageManager.RunRemove`, `FileSystemPackageStore.Delete` | Deletes the install root; errors if not installed. |
| Transactional staging + rollback | [Managing MSIX deployment](https://learn.microsoft.com/en-us/windows/msix/desktop/managing-your-msix-deployment-overview) | yes | `FileSystemPackageStore.CommitLocked`, `DurableFileSystem` | Staged files and directories are flushed before an integrity-checked, phase-aware intent journal orders and idempotently recovers promotion or rollback. |
| Async progress / status reporting | [DeploymentResult](https://learn.microsoft.com/en-us/uwp/api/windows.management.deployment.deploymentresult) | yes | `Deployment/MsixResponse.cs`, `InstallationStep.cs`, `SynchronousProgress.cs` | Reports staged progress (Started → GetPackageInformation → Extraction → Integration → Completed) and a completion task. |
| Handler pipeline (extraction + OS integration) | [Original MSIX Core handlers](https://github.com/microsoft/msix-packaging/tree/master/MsixCore) | partial | `Deployment/Handlers/IPackageHandler.cs` | Interface + `PackageDeploymentContext` exist; extraction is currently inlined in `RunAdd` rather than run through pluggable handlers, and no OS-integration handlers are registered. |
| Version / downgrade policy | [DeploymentOptions](https://learn.microsoft.com/en-us/uwp/api/windows.management.deployment.deploymentoptions) | yes | `FileSystemPackageStore.CommitLocked`, `DeploymentOptions.cs` | One version per family; upgrades replace, downgrades require `AllowDowngrade`, duplicates require `ForceReinstall`; `ForceApplicationShutdown` retains process-shutdown semantics only. |
| OS integration: Start Menu shortcuts | [MSIX Core install handlers](https://github.com/microsoft/msix-packaging/tree/master/MsixCore) | no | — | — |
| OS integration: Add/Remove Programs registration | [MSIX Core install handlers](https://github.com/microsoft/msix-packaging/tree/master/MsixCore) | no | — | — |
| OS integration: file-type / protocol registration | [MSIX Core install handlers](https://github.com/microsoft/msix-packaging/tree/master/MsixCore) | no | — | Depends on manifest extension parsing (§3), also missing. |

---

## CLI (`msixmgr`) surface, for context

- `inspect` (`InspectCommand.cs`): identity, family/full name, version, arch, display/publisher name,
  capabilities, signed flag, block-map file count/hash method. Text or `--json`.
- `validate` (`ValidateCommand.cs`): block-map verification + coverage, and — when signed — CMS
  envelope integrity and publisher/subject agreement. It **explicitly warns** that signature binding
  (APPX indirect-data digests) and certificate trust are not verified, so a pass is an *integrity*
  verdict, not an *authenticity* one. CI-friendly exit codes (0 ok, 1 fail, 2 usage).
- `unpack` (`UnpackCommand.cs`): extracts a package to a loose layout via `PackageExtractor`
  (`unpack <path> -Destination <dir> [--json]`), cross-platform, no install/OS integration.
- `PackageManager.AddPackage` / `RemovePackage` are now **implemented** (see §8) but not yet wired to
  dedicated `msixmgr` verbs, so they are not advertised by CLI help.
