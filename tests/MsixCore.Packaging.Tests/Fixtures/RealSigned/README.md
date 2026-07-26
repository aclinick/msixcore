# Real-Signed Test Fixtures

These `.msix` packages were produced by real Microsoft tooling (SignTool via `winapp sign`) and are used to validate that our APPX indirect-data digest binding implementation correctly parses and verifies signatures created by production signing tools.

## How they were produced (2026-07-25)

1. **Create a loose package layout** (`C:\Temp\msix-sign-test\src`) with `AppxManifest.xml`, `Assets/hello.txt`, and `Assets/Square150x150Logo.png`.

2. **Pack:**
   ```
   msixmgr pack --directory C:\Temp\msix-sign-test\src --package C:\Temp\msix-sign-test\SignTest.msix
   ```

3. **Generate a self-signed dev certificate:**
   ```
   winapp cert generate --publisher "CN=MsixCoreTest"
   ```
   Certificate: `CN=MsixCoreTest`, self-signed, valid 2026-07-25 to 2027-07-25.

4. **Sign:**
   ```
   winapp sign C:\Temp\msix-sign-test\SignTest.msix
   ```
   SignTool exit 0, "Successfully signed".

5. **Create the stapling attack fixture:**
   - Built a second package from a different source layout (`srcB`) with different payload.
   - Replaced the `AppxSignature.p7x` entry in the second package with the one from `SignTest.msix`.
   - Result: `Stapled.msix` — a tampered package with a real but stolen signature.

## What they contain

| File | Description |
|------|-------------|
| `SignTest.msix` | Legitimately signed package. 6 ZIP entries. Signature is valid. |
| `Stapled.msix` | Tampered package with the signature from `SignTest.msix` stapled in. CMS envelope is valid (it is genuine) but AXCT/AXBM digests mismatch (content differs). |

## Observed signature properties

- P7X prefix: `50 4B 43 58` (PKCX) ✓
- CMS content type OID: `1.3.6.1.4.1.311.2.1.4` (SPC_INDIRECT_DATA_CONTENT) ✓
- Digest table: 148 bytes = 4 entries (AXPC, AXCD, AXCT, AXBM), no AXCI
- Algorithm: SHA-256

## Security notes

- **No private keys are committed.** The `.msix` files contain only the public certificate inside the CMS envelope.
- Tests must NOT depend on certificate validity dates or trust chain verification. The cert expires 2027-07-25. Assert only on binding and CMS envelope integrity.

## Regeneration

If these need to be regenerated (e.g. after changes to the packing format), follow steps 1–5 above. The signing cert can be any self-signed cert; the specific thumbprint does not matter as we do not verify trust.
