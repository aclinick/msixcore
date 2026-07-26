# APPX signature format: real SignTool validation

Date: 2026-07-25. Branch `feature/ax-binding`.

## Summary

The AX* digest table format assumptions were validated end-to-end against a real SignTool-produced signature. A genuine `winapp sign` (SignTool) signed package was used to confirm every implementation assumption, then a stapling attack was reproduced and confirmed defeated.

## Procedure

1. `msixkit pack` built a package from a loose layout.
2. `winapp cert generate --publisher "CN=MsixCoreTest"` created a self-signed dev cert.
3. `winapp sign` (SignTool) signed the package — exit 0.
4. `AppxSignature.p7x` was dumped and compared against implementation assumptions.
5. The signature was lifted and stapled onto a tampered package.
6. Validation was run on both `main` (no binding) and `feature/ax-binding`.

## Observed values (confirmed matching implementation)

| Property | Expected | Observed |
|----------|----------|----------|
| P7X prefix | `50 4B 43 58` (PKCX) | ✓ matches |
| CMS content type OID | `1.3.6.1.4.1.311.2.1.4` | ✓ matches |
| Digest table length | 148 bytes (4 entries) | ✓ matches |
| Entry tags | AXPC, AXCD, AXCT, AXBM (in order) | ✓ matches |
| AXCI presence | absent (no CodeIntegrity.cat) | ✓ confirms optional |
| AXCT digest | SHA-256 of decompressed `[Content_Types].xml` | ✓ byte-for-byte match |
| AXBM digest | SHA-256 of decompressed `AppxBlockMap.xml` | ✓ byte-for-byte match |

## Attack reproduction results

| Branch | Package | Block map | CMS | Binding | Exit |
|--------|---------|-----------|-----|---------|------|
| `main` (pre-binding) | Stapled.msix | OK | OK | not checked | 0 ← **vulnerable** |
| `feature/ax-binding` | Stapled.msix | OK | OK | FAILED (AXCT+AXBM mismatch) | 1 ← **fixed** |
| `feature/ax-binding` | SignTest.msix | OK | OK | verified (AXCT+AXBM valid) | 0 |

## Implications

- The format specification derived from `microsoft/msix-packaging` source is **confirmed correct** against real tooling output.
- Synthetic test fixtures remain valid for unit tests but no longer the only evidence of correctness.
- Real fixtures are committed at `tests/MsixCore.Packaging.Tests/Fixtures/RealSigned/`.

## Remaining synthetic-only areas

- AXCI (5-entry table) — no real package with `CodeIntegrity.cat` was available for testing.
- AXPC/AXCD byte ranges — still unverified (exact ZIP byte ranges not recoverable from public spec).
- `AlgorithmIdentifier` with explicit NULL vs absent parameters — both paths exist in synthetic tests; only one form was observed in the real signature (implementation handles both).
