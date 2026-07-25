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

---

## P0 — Correctness & security of existing features

### P0-1. Signature binding is not verified (authenticity gap)
**Gap:** `PackageSignatureReader` verifies only CMS envelope integrity
(`CheckSignature(verifySignatureOnly:true)`). It never checks the APPX SIP indirect-data digests
(`AXPC` package hash, `AXCT` content-types hash, `AXBM` block-map hash, `AXCI` code-integrity hash,
`AXCF` central-directory hash) that bind the signature to the actual bytes. A package can therefore
carry a structurally-valid signature that does **not** match its content and still pass CMS integrity.
Ref: [Package integrity enforcement](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview).

**Test cases:**
- **TC-P0-1a (negative binding):** Take a validly signed package; swap `AppxBlockMap.xml` for a
  different (self-consistent) block map without re-signing. Assert a future `VerifySignatureBinding`
  reports **AXBM mismatch** (today: CMS integrity still passes — document as a known false pass).
- **TC-P0-1b (tampered payload):** Modify one payload file and regenerate its block-map hash but not
  the signature. Assert binding verification fails on `AXBM`.
- **TC-P0-1c (happy path):** A correctly signed package asserts all indirect-data digests match.
- **TC-P0-1d (regression guard):** Assert `ValidationReport.SignatureBindingVerified == false` today
  so the "not an authenticity guarantee" contract is locked until binding lands.

### P0-2. Publisher/subject DN comparison can false-mismatch on DER encoding
**Gap:** `PackageSignature.MatchesPublisher` compares `X500DistinguishedName(manifestPublisher).RawData`
to the signer subject's `RawData`. Re-encoding the manifest string can pick a different ASN.1 string
type (UTF8String vs PrintableString) than the certificate used, producing a false "does not match"
even for a legitimately-matching package. **Filed as a bug** (see bottom).

**Test cases:**
- **TC-P0-2a:** Manifest `Publisher="CN=Contoso"` + certificate whose subject `CN=Contoso` is encoded
  as `PrintableString`. Assert `MatchesPublisher` returns `true`.
- **TC-P0-2b:** Same CN encoded as `UTF8String`. Assert `true`.
- **TC-P0-2c (true mismatch):** Manifest `CN=Contoso` vs signer `CN=Contoso2`. Assert `false`.
- **TC-P0-2d (multi-RDN order/spacing):** `CN=Contoso, O=Contoso, C=US` with reordered/space
  variations that are semantically equal. Assert `true`.

### P0-3. Block-map compressed `Size` (LFH) is parsed but unenforced
**Gap:** `BlockMapBlock.CompressedSize` is read but never checked against the ZIP local file header,
so a mismatch between declared stored size and actual entry size goes undetected.
Ref: [AppxBlockMap.xml](https://learn.microsoft.com/en-us/windows/msix/overview).

**Test cases:**
- **TC-P0-3a:** Compressed file whose block `Size` attributes disagree with the actual deflate stream
  length. Assert (future) LFH validation flags it; today assert content hash still guards correctness.
- **TC-P0-3b (uncompressed/stored):** File stored (no compression, no per-block `Size`). Assert
  verification passes.
- **TC-P0-3c (empty file):** Zero-byte file (`Size="0"`, no `Block`). Assert valid (regression guard —
  the current verifier handles this correctly).

### P0-4. `[Content_Types].xml` is never parsed/validated
**Gap:** Content-types map is excluded from block-map coverage but never validated, so a package can
omit required default/override content types or declare parts with no content type.
Ref: [MSIX overview](https://learn.microsoft.com/en-us/windows/msix/overview).

**Test cases:**
- **TC-P0-4a:** Package missing `[Content_Types].xml`. Assert a diagnostic (today: silently ignored).
- **TC-P0-4b:** Payload part with an extension not covered by any `Default`/`Override`. Assert a
  content-type coverage error.

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

---

## P1 — High-value modern MSIX surface

### P1-1. Bundle reading is not wired into the package reader
**Gap:** `BundleManifestParser` exists but nothing opens `AppxMetadata/AppxBundleManifest.xml` or
detects a `.msixbundle`. Ref:
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

### P1-5. Install engine: extract → stage → commit
**Gap:** `AddPackage`/`RemovePackage` throw `NotImplementedException`; no extraction/staging.
Ref: [Managing MSIX deployment](https://learn.microsoft.com/en-us/windows/msix/desktop/managing-your-msix-deployment-overview).

**Test cases:**
- **TC-P1-5a:** `AddPackage` (ExtractOnly) unpacks all block-map files to the store, hashes match, and
  the package becomes discoverable via `FindPackage`.
- **TC-P1-5b:** `AddPackage` verifies the block map **before** commit; a tampered package leaves the
  store unchanged (transactional rollback).
- **TC-P1-5c:** `RemovePackage` deletes the install root and the package is no longer found.
- **TC-P1-5d:** Re-add same full name → idempotent/higher-version replace; lower version rejected
  unless `ForceApplicationShutdown`-style override.
- **TC-P1-5e:** Handler pipeline runs handlers in order on add and reverse on remove (use a fake
  handler to record call order).

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

- **P0-2 (publisher DN encoding):** filed as
  [aclinick/msixcore#12](https://github.com/aclinick/msixcore/issues/12).
  Documents that `PackageSignature.MatchesPublisher` can report a false mismatch when the manifest
  `Publisher` string re-encodes to a different ASN.1 string type than the signer certificate's
  subject, causing `validate` to wrongly flag a legitimately-signed package.
