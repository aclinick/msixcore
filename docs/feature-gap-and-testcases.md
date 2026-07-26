# Feature-gap analysis & conformance test cases

Companion to [`msix-spec-coverage.md`](./msix-spec-coverage.md). This file prioritizes the gaps
between the current `msixcore` port and "modern MSIX" parity, and — for each gap — proposes concrete
**test cases** (what package/manifest to build, what to assert). These feed the future `xc-testsuite`
conformance corpus.

Prioritization rubric:

- **P0** — correctness/security of what the port already claims to do (a wrong "valid" verdict is
  worse than an unsupported feature).
- **P1** — high-value modern MSIX surface most packages actually use (bundles, extensions,
  dependencies, install engine).
- **P2** — completeness for less-common package kinds and advanced scenarios.

Each test case lists a fixture (the package/manifest to synthesize) and the assertion. Fixtures
should be minimal, generated deterministically, and committed under a `fixtures/` tree so they run in
Linux CI (the whole reader is cross-platform).

> **Status refresh (post signature-binding and read-path validation work):** P0-1 through P0-4 are
> now **resolved** and retained as regression suites. The install engine (P1-5), OPC
> percent-decoding, `CodeIntegrity.cat` footprint handling, and bundle parsing have also landed.

---

## P0 — Correctness & security of existing features

### P0-1. Signature binding — RESOLVED
**Was:** CMS envelope integrity was checked without binding the signature to package content.
**Fixed (f03de1c):** the APPX SIP indirect-data table is parsed and `AXCT`, `AXBM`, and optional
`AXCI` digests are verified against cached bytes from the package. `AXPC` and `AXCD` are parsed and
reported as not verified because their exact ZIP byte ranges are not yet reconstructed. Certificate
trust-chain validation remains intentionally separate.
Ref: [Package integrity enforcement](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview).

**Test cases (regression):**
- **TC-P0-1a (negative binding):** Take a validly signed package; swap `AppxBlockMap.xml` for a
  different (self-consistent) block map without re-signing. Assert `AXBM` mismatch and an invalid
  validation report.
- **TC-P0-1b (tampered payload):** Modify one payload file and regenerate its block-map hash but not
  the signature. Assert CMS can remain internally consistent but `AXBM` binding fails.
- **TC-P0-1c (contract guard):** Assert `ValidationReport.SignatureBindingVerified == true` for a
  correctly digest-bound signature and `false` when a verified footprint digest mismatches.

### P0-2. Publisher/subject DN comparison — RESOLVED ([#12](https://github.com/aclinick/msixcore/issues/12))
**Was:** `MatchesPublisher` re-encoded the manifest string and byte-compared DER, so a certificate
whose subject used a different ASN.1 string type (e.g. `PrintableString` vs `UTF8String`) produced a
false "does not match" for a legitimately-signed package.
**Fixed:** `PackageSignatureReader` now captures `SubjectNameRawData` (the certificate's original
subject DER), and `MatchesPublisher` decodes both DNs into RDNs and compares each RDN's attribute
**type (OID)** and **decoded value**, so the result is independent of the underlying string encoding
and faithful to RDN order. These test cases become **regression guards**:

**Test cases (regression):**
- **TC-P0-2a:** Manifest `Publisher="CN=Contoso"` + certificate whose subject `CN=Contoso` is encoded
  as `PrintableString`. Assert `MatchesPublisher` returns `true`.
- **TC-P0-2b:** Same CN encoded as `UTF8String`. Assert `true`.
- **TC-P0-2c (true mismatch):** Manifest `CN=Contoso` vs signer `CN=Contoso2`. Assert `false`.
- **TC-P0-2d (multi-RDN order/spacing):** `CN=Contoso, O=Contoso, C=US` with reordered/space
  variations. Assert equal when the RDN sequence matches; assert `false` when RDN **order** differs
  (order is significant in a DN).
- **TC-P0-2e (multi-valued RDN):** A DN with a multi-valued RDN (e.g. `CN=A+OU=B`) exercises the
  raw-encoding fallback path; assert correct equal/not-equal outcome.

### P0-3. Block-map compressed `Size` validation — RESOLVED
**Fixed:** `BlockMapVerifier` compares canonical ZIP metadata supplied through `IOpcPackage` with the
block map. Stored entries reject per-block `Size`; compressed entries require the sum to equal the ZIP
compressed length or be exactly two bytes smaller for the MakeAppx `Z_FULL_FLUSH` terminator.
Duplicate files and non-empty zero-block declarations are rejected. Loose directories return no ZIP
metadata, so this container-specific comparison is not applicable there.
Ref: [AppxBlockMap.xml](https://learn.microsoft.com/en-us/windows/msix/overview).

**Test cases (regression):**
- **TC-P0-3a:** Compressed file whose block `Size` attributes disagree with the actual deflate stream
  length. Assert compressed-size validation flags it.
- **TC-P0-3b (uncompressed/stored):** File stored (no compression, no per-block `Size`). Assert
  verification passes.
- **TC-P0-3c (empty file):** Zero-byte file (`Size="0"`, no `Block`). Assert valid (regression guard —
  the verifier handles this correctly).
- Assert the two-byte `Z_FULL_FLUSH` allowance passes, any other discrepancy fails, duplicate file
  declarations fail, and a non-empty file with zero blocks fails.

### P0-4. `[Content_Types].xml` validation — RESOLVED
**Fixed:** the hardened `ContentTypesParser` reads OPC `Default` and canonicalized `Override`
declarations, and `BlockMapVerifier` requires the part and content-type coverage for every package
part. `[Content_Types].xml` is forbidden from the block map. `AppxMetadata/CodeIntegrity.cat` remains
a footprint part excluded from block-map coverage; its content type is still required.
Ref: [MSIX overview](https://learn.microsoft.com/en-us/windows/msix/overview).

**Test cases (regression):**
- **TC-P0-4a:** Package missing `[Content_Types].xml`. Assert a diagnostic.
- **TC-P0-4b:** Payload part with an extension not covered by any `Default`/`Override`. Assert a
  content-type coverage error.
- **TC-P0-4c (regression):** Package containing `AppxMetadata/CodeIntegrity.cat` passes block-map
  coverage (the catalog is a footprint part, not listed in the block map).
- Assert `[Content_Types].xml` listed in the block map fails and a canonical `Override` can cover a
  part whose extension has no `Default`.

### P0-5. OPC hardening regression guards (already implemented — lock them in)
**Gap:** none functionally, but the security-critical part-name rules deserve explicit corpus tests.

**Test cases:**
- **TC-P0-5a (zip-slip):** Entry named `../evil.exe`. Assert `OpcPackage.Open` throws
  `InvalidDataException`.
- **TC-P0-5b (rooted):** Entry `/abs/x`. Assert rejected.
- **TC-P0-5c (backslash):** Entry `a\b`. Assert rejected.
- **TC-P0-5d (dup case-insensitive):** `App.dll` and `app.dll`. Assert duplicate rejected.
- **TC-P0-5e (loose symlink):** Directory package with a symlink escaping root. Assert skipped/rejected.
- **TC-P0-5f (XXE):** Manifest/block map with a DOCTYPE + external entity. Assert parse fails safely
  (DTD prohibited) and no network/file fetch occurs.
- **TC-P0-5g (percent-encoded part name):** Entry `foo%21.txt` canonicalizes to `foo!.txt` and is
  found by that logical name (`OpcPackage.TryCanonicalizePartName`).
- **TC-P0-5h (encoded separator/traversal/control):** Entries `a%2fb`, `%2e%2e/evil`, and `x%00y`
  are each rejected as invalid part names (decoding must not smuggle in a boundary/traversal/NUL).

---

## P1 — High-value modern MSIX surface

### P1-1. Bundle reading — IMPLEMENTED
**Done:** `MsixPackage.IsBundle` detects bundle containers, `MsixBundle.Open`
parses `AppxMetadata/AppxBundleManifest.xml`, and `MsixPackage.Open` reports a
specific type-mismatch error. Ref:
[Bundle manifest schema](https://learn.microsoft.com/en-us/uwp/schemas/bundlemanifestschema/root-elements-bundle-manifest).

**Test cases:**
- **TC-P1-1a:** Open a `.msixbundle`; assert `IsBundle == true` and the child app/resource packages are
  enumerated with correct Type/Version/Architecture/ResourceId.
- **TC-P1-1b:** Assert bundle `inspect` prints child packages; a plain `.msix` reports `IsBundle == false`.
- **TC-P1-1c:** Malformed bundle (empty `Packages`) surfaces `InvalidDataException` (parser already
  enforces this — add a fixture).

### P1-2. Bundle applicability / resource selection
**Gap:** No engine selects the applicable app package (by architecture) and applicable resource
packages (by language/scale/DXFL) for a target. Ref:
[Resource management](https://learn.microsoft.com/en-us/windows/uwp/app-resources/resource-management-system).

**Test cases:**
- **TC-P1-2a:** Bundle with x86/x64/arm64 app packages; for a target of x64 assert only the x64 app is
  selected.
- **TC-P1-2b:** Resource packages `en-US`, `fr-FR`, scale-200/400; for `fr-FR`+scale-200 assert the
  matching resource packages are chosen and non-matching excluded.
- **TC-P1-2c:** No applicable architecture → assert a clear "no applicable package" error.

### P1-3. Manifest dependencies (framework / main / host runtime)
**Gap:** Only `TargetDeviceFamily` is parsed; `PackageDependency`, `uap4:MainPackageDependency`, and
`uap10:HostRuntimeDependency` are not. Ref:
[PackageDependency](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-f-packagedependency).

**Test cases:**
- **TC-P1-3a:** Manifest with `PackageDependency Name=Microsoft.VCLibs... MinVersion=... Publisher=...`;
  assert all three fields are parsed into a dependency model.
- **TC-P1-3b:** Modification package manifest with `uap4:MainPackageDependency`; assert the main-package
  relationship is captured.
- **TC-P1-3c:** Host-runtime-dependent app; assert `HostRuntimeDependency` parsed.
- **TC-P1-3d (unsatisfied):** Install-time resolution reports a missing framework dependency.

### P1-4. Manifest extensions (declarations only)
**Gap:** No extension category is parsed (§3 of coverage). First step: parse declarations so tooling
can report them, before OS registration. Ref:
[Desktop extensions](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions).

**Test cases (parse + surface, one fixture each):**
- **TC-P1-4a:** `uap:FileTypeAssociation` (name + `.ext` list) → assert associations parsed.
- **TC-P1-4b:** `uap:Protocol Name="myscheme"` → assert protocol parsed.
- **TC-P1-4c:** `uap5:AppExecutionAlias` with an executable alias → assert alias parsed.
- **TC-P1-4d:** `desktop:Extension` `windows.startupTask` → assert startup task parsed.
- **TC-P1-4e:** `com:Extension` out-of-process server (CLSID) → assert COM server parsed.
- **TC-P1-4f:** `desktop:Extension` shortcut / `windows.fullTrustProcess` → assert parsed.
- **TC-P1-4g (round-trip):** Package with several extensions; `inspect --json` lists them all.

### P1-5. Install engine — IMPLEMENTED; remaining: pluggable handlers and OS integration
**Done:** `AddPackage`/`RemovePackage` now run a real extract → stage → commit pipeline
(`PackageManager.RunAdd`/`RunRemove`) over `IPackageStore`. `FileSystemPackageStore` implements
`CreateStagingLocation`/`Commit` (move-aside backup + atomic promote + rollback) and `Delete`; payloads
are hashed while extracted and committed only after validation; progress is surfaced via `IMsixResponse`/`InstallationStep`;
`PackageExtractor` provides containment-checked loose extraction. The test cases below are now
**runnable regression tests**. Ref:
[Managing MSIX deployment](https://learn.microsoft.com/en-us/windows/msix/desktop/managing-your-msix-deployment-overview).

**Test cases (regression — should pass today):**
- **TC-P1-5a:** `AddPackage` unpacks all block-map files to the store, hashes match, and the package
  becomes discoverable via `FindPackage`.
- **TC-P1-5b:** `AddPackage` verifies the block map **before** commit; a tampered package leaves the
  store unchanged and the pre-existing install (if any) intact (rollback).
- **TC-P1-5c:** `RemovePackage` deletes the install root; the package is no longer found; removing a
  non-installed full name reports failure.
- **TC-P1-5d:** Re-add an already-installed full name without `ForceReinstall` fails; with it, the
  reinstall succeeds. `ForceApplicationShutdown` does not change version policy.
- **TC-P1-5g:** Upgrades replace the installed family; downgrades fail unless `AllowDowngrade` is set.
- **TC-P1-5h:** A cross-process lock file serializes commits sharing one store root.
- **TC-P1-5e (cancellation):** Cancelling mid-extraction leaves no committed install and cleans up the
  staging directory.

**Remaining gaps (still open):**
- **TC-P1-5f (pluggable handlers):** Extraction is inlined in `RunAdd`, not run through
  `IPackageHandler` handlers ordered on add / reversed on remove. Assert (future) a fake handler's
  call order once the pipeline is wired.

### P1-6. Richer VisualElements / capability categorization
**Gap:** VisualElements omits many logos/tiles; capabilities aren't categorized by type/namespace.

**Test cases:**
- **TC-P1-6a:** Manifest with wide/large/small logos, `DefaultTile`, `SplashScreen`; assert each parsed.
- **TC-P1-6b:** Capabilities mixing `Capability`, `DeviceCapability`, `rescap:Capability`,
  `uap:Capability`, `CustomCapability`; assert each is categorized (not just name-collected).
- **TC-P1-6c (restricted):** `runFullTrust` (restricted) is flagged as restricted-capability.

---

## P2 — Completeness for advanced package kinds

### P2-1. Optional & modification packages
Ref: [Optional packages](https://learn.microsoft.com/en-us/windows/msix/package/optional-packages),
[Modification packages](https://learn.microsoft.com/en-us/windows/msix/modification-package-authoring/modification-package).
- **TC-P2-1a:** Optional package manifest → assert recognized as optional and linked to its main package.
- **TC-P2-1b:** Modification package → assert recognized; install requires the main package present.

### P2-2. Sparse / external-location packages
Ref: [Grant identity to non-packaged apps](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps).
- **TC-P2-2a:** Manifest with `uap10:AllowExternalContent`/external location → assert parsed and the
  external-content model captured.

### P2-3. Framework & resource package roles
Ref: [Framework packages](https://learn.microsoft.com/en-us/windows/msix/framework-packages/framework-packages-overview).
- **TC-P2-3a:** Framework package → assert `IsFramework` and that a dependent app resolves against it.
- **TC-P2-3b:** Resource package with `ResourceId` → assert role + applicability qualifiers modeled.

### P2-4. Signature: trust chain, timestamp, multi-signer, catalog
Ref: [Signing overview](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview).
- **TC-P2-4a:** Trust-chain evaluation against a supplied root store (opt-in) → trusted vs untrusted.
- **TC-P2-4b:** Timestamp countersignature present/expired → assert validity independent of cert expiry.
- **TC-P2-4c:** Package with multiple signers → assert all are surfaced.
- **TC-P2-4d:** `CodeIntegrity.cat` present → assert catalog is read and bound.

### P2-5. Encrypted packages & Zip64 scale
- **TC-P2-5a:** `.emsix` → assert a clear "encrypted packages unsupported" diagnostic (not a crash).
- **TC-P2-5b:** Zip64 package (>4 GB or >65535 entries) → assert parts enumerate correctly.

---

## Suggested corpus layout for `xc-testsuite`

```
fixtures/
  opc/            # zip-slip, rooted, backslash, dup-name, xxe, symlink-loose
  manifest/       # identity edge cases, versions, capabilities, visualelements, dependencies, extensions
  blockmap/       # sha256/384/512, empty file, size mismatch, coverage over/under
  signature/      # good, tampered-blockmap, wrong-publisher, printable-vs-utf8 DN, multi-signer
  bundle/         # main+resource, multi-arch, applicability by lang/scale/dxfl, malformed
  kinds/          # framework, resource, optional, modification, sparse, hostruntime
  deploy/         # add/remove/rollback, handler-order
```

Each fixture pairs a generator (so bytes are reproducible in CI) with an `expected.json` capturing the
asserted outcome (identity, block-map verdict, signature verdict, parsed extensions, applicability
result). Negative fixtures assert the specific exception type/message.

---

## Bugs filed against `aclinick/msixcore`

- **P0-2 (publisher DN encoding):** [aclinick/msixcore#12](https://github.com/aclinick/msixcore/issues/12)
  — **FIXED/merged**. `PackageSignature.MatchesPublisher` now compares decoded RDNs from the
  certificate's raw subject bytes (encoding- and order-faithful), eliminating the false-mismatch that
  wrongly flagged legitimately-signed packages. Retained above as regression test cases TC-P0-2a…e.
- **Cross-process store coordination:** tracked as issue #14 (referenced in
  `FileSystemPackageStore`); the in-process promotion lock does not coordinate separate processes over
  a shared store root. See TC-P1-5h.

This refresh records the resolved signature-binding, ZIP-size, and OPC content-type gaps alongside
the earlier install engine, extractor, canonical part-name, catalog-footprint, and DN-matching work.
