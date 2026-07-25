# Windows OS AppX acceptance of MSIX Core-authored packages

Date: 2026-07-25.  Machine: Windows_NT, Windows SDK makeappx/signtool 10.0.26100.0.  Worktree: `D:\source\msixcore-osaccept`, branch `feature/os-acceptance`.

## Bottom line

The Windows OS AppxPackaging COM reader **does accept packages authored by MSIX Core (.NET)**.  For two real packages repacked from normalized source, both MSIX Core `Stored` and `Optimal` outputs opened successfully, exposed the manifest identity, enumerated payload files, and returned the block map.

For the OS reader, **ZIP64 is not required, data descriptors are not required, and the UTF-8 general-purpose bit is tolerated**.  The ablation matrix also shows the reader accepts ZIP64, data descriptors, both together, and clearing the UTF-8 bit.

Full `Add-AppxPackage` registration was time-boxed after the reader matrix.  After scope correction, the install subject was the user's own `CcProto` package only.  `CcProto` was not installed before the test, so the real identity path was used and each attempt was followed by `Remove-AppxPackage` if anything registered.  The non-elevated environment still could not complete signed installation: the makeappx-produced control and the MSIX Core variants failed identically with AppX deployment certificate-trust policy (`0x800B0109`).  That makes Tier 2 **blocked by environment/policy**, not by MSIX Core ZIP structure.

## Controls and real packages

Real input packages used from `C:\Users\andre\Downloads`:

| Case | Original package | Size | Reader control |
|---|---|---:|---|
| `war-x64` | `CcProto_1.0.2.0_x64_Debug_Test\...\Dependencies\x64\Microsoft.WindowsAppRuntime.1.8-experimental3.msix` | 24.37 MB | OK; `Microsoft.WindowsAppRuntime.1.8-experimental3_8000.526.1808.0_x64__8wekyb3d8bbwe`, 277 payload files, block map read |
| `ccproto` | `CcProto_1.0.2.0_x64_Debug_Test\...\CcProto_1.0.2.0_x64_Debug.msix` | 48.74 MB | OK; `fruitybunny.CcProto_1.0.2.0_x64__rf6ttnqpqgr0w`, 306 payload files, block map read |

Each input was normalized with `SourceNormalizer`, then packed three ways: original control, `makeappx`, MSIX Core `Stored`, MSIX Core `Optimal`.

## Tier 1: OS AppxPackaging reader results

Reader operation: `IAppxFactory::CreatePackageReader`, `IAppxPackageReader::GetManifest`, payload enumeration via `GetPayloadFiles`, and block-map stream read via `GetBlockMap().GetStream()`.

| Case | Package | Result | HRESULT/error |
|---|---|---|---|
| `war-x64` | original | OK | none |
| `war-x64` | makeappx control | OK | none |
| `war-x64` | MSIX Core Stored | OK | none |
| `war-x64` | MSIX Core Optimal | OK | none |
| `ccproto` | original | OK | none |
| `ccproto` | makeappx control | OK | none |
| `ccproto` | MSIX Core Stored | OK | none |
| `ccproto` | MSIX Core Optimal | OK | none |

## Tier 1 ablation matrix

Variants were post-processed from MSIX Core `Optimal` output without changing payload bytes or local header sizes recorded in the block map:

- A / `baseline`: current MSIX Core shape: version-needed 2.0, CRC/sizes in local headers, no ZIP64 EOCD, UTF-8 bit set.
- B / `zip64`: baseline plus version-needed 4.5, central ZIP64 extra field `0x0001` with 24 bytes, ZIP64 EOCD and locator.
- C / `descriptor`: baseline plus GP bit 3, zero local CRC/sizes, `PK\x07\x08` data descriptor with 32-bit sizes after data.
- D / `both`: B + C.
- E / `no-utf8`: baseline with GP bit `0x0800` cleared.

| Case | A baseline | B ZIP64 | C descriptors | D both | E no UTF-8 |
|---|---|---|---|---|---|
| `war-x64` reader | OK | OK | OK | OK | OK |
| `ccproto` reader | OK | OK | OK | OK | OK |

All cells returned exit code 0 from the OS reader helper; no HRESULT/error was emitted.

## Tier 2 install status: blocked by environment/policy, not ZIP structure

After the user's scope correction, install testing was repeated with `CcProto_1.0.2.0_x64_Debug.msix`, which is the user's own app and is safe for install testing.  Pre-check:

```powershell
Get-AppxPackage -Name '*CcProto*'
```

returned no installed CcProto package, so the real identity `fruitybunny.CcProto_1.0.2.0_x64__rf6ttnqpqgr0w` was used.  No pre-existing CcProto was removed.

The original CcProto package is signed by `CN=Fruit`, but that certificate expired on 2026-06-29.  Repacked packages were therefore signed with a fresh 7-day self-signed certificate.  The final plumbing retry staged under `%USERPROFILE%\osaccept`, called `Test-Path` immediately before `Add-AppxPackage`, ran `Unblock-File`, tried both an absolute path and a `file://` URI, and verified the signature before every install attempt.

- Subject: `CN=Fruit`
- Thumbprints used during retries: `157125F316332997849C3587836AFB8C06FE4D93`, then `6C2D03BF8F5DD16DEB434082ED80BBE4BC7B8A85`
- Stores touched: CurrentUser `My`, `TrustedPeople`, and `Root`
- Attempt to add the cert to LocalMachine `Root`: failed without elevation, `0x80070005 ERROR_ACCESS_DENIED`
- Manifest Publisher: `CN=Fruit`; cert Subject: `CN=Fruit`; exact match: `True`
- `signtool verify /pa /v`: success for the makeappx control, baseline, and both
- OS AppxPackaging reader: OK for the makeappx control, baseline, and both

Install matrix:

| Package / path mode | `Test-Path` | Install result | Exact failure |
|---|---|---|---|
| makeappx control, absolute path | True | FAIL | Cmdlet wrapper `0x80070002`; deployment `0x80073CF0`; specific error `0x800B0109: The root certificate of the signature in the app package or bundle must be trusted`; AppXDeploymentServer `[404] ... 0x80073CF0`, `[402] error 0x87E80034`, `[495] GetManifestReader ... 0x87E80034` |
| makeappx control, `file://` URI | True | FAIL | Same `0x800B0109` trust failure |
| A baseline, absolute path | True | FAIL | Same `0x800B0109` trust failure |
| A baseline, `file://` URI | True | FAIL | Same `0x800B0109` trust failure |
| D both, absolute path | True | FAIL | Same `0x800B0109` trust failure |
| D both, `file://` URI | True | FAIL | Same `0x800B0109` trust failure |

Interpretation: the makeappx-produced control fails through the exact same install path, with the same trust error as MSIX Core baseline and the makeappx-style `both` variant.  Therefore Tier 2 is blocked by certificate trust/environment in this non-elevated shell, not by MSIX Core's ZIP shape.  Earlier `-AllowUnsigned` attempts also failed by unsigned-package policy (`0x80073D2C` publisher not in unsigned namespace; `0x80073D2B` invalid unsigned content), and those policy failures were identical across variants including `both`.

Follow-up after fixing the descriptor-only variant generator to emit classic 32-bit data-descriptor sizes when ZIP64 is disabled: the CcProto descriptor-only package now opens successfully through the OS reader both unsigned and after `signtool` signing.  The previous signed descriptor-only `0x80511007` reader failure was caused by the malformed experimental variant (64-bit descriptor sizes in a non-ZIP64 archive), not by data descriptors or signing.

## Recommendation

1. **Correctness: fix OPC escaping for bracketed and non-ASCII payload names.**  `[Content_Types].old` must be stored as `%5BContent_Types%5D.old`, and non-ASCII names such as `é.txt` must be stored as UTF-8 byte percent escapes (`%C3%A9.txt`).  These are independent of the ZIP64/data-descriptor question and are now fixed in `OpcPartNameEncoder` with regression tests.
2. **Cosmetic for normal-size packages: makeappx's always-ZIP64 output.**  The OS reader accepted non-ZIP64 baseline packages and ZIP64 variants.  Implement ZIP64 only when needed for >4 GiB offsets/sizes or >65,535 entries, or if a future compatibility target proves it necessary.
3. **Cosmetic for reader compatibility: makeappx's data descriptors.**  The OS reader accepted baseline packages without descriptors and accepted corrected descriptor-only variants both unsigned and signed.
4. **Cosmetic/tolerated: UTF-8 general-purpose bit.**  Baseline with bit `0x0800` set and variant E with it cleared both opened successfully for these ASCII-only package paths.
5. **No writer change recommended for ZIP version-needed/local CRC+sizes/EOCD shape.**  Those differences are tied to ZIP64/descriptors and were accepted by the OS reader.
6. **Follow-up if full install proof is required:** run the same committed commands from an elevated shell, import the test certificate into the machine trust store, then repeat the signed CcProto `Add-AppxPackage` matrix.  The current non-elevated run could not alter machine trust and therefore could not complete signed installation.

## Repro commands

```powershell
git -C D:\source\msixcore worktree add -b feature/os-acceptance D:\source\msixcore-osaccept origin/main

dotnet build D:\source\msixcore-osaccept\tools\MsixCore.OSAcceptance\MsixCore.OSAcceptance.csproj -c Release --nologo
$tool = 'D:\source\msixcore-osaccept\tools\MsixCore.OSAcceptance\bin\Release\net10.0-windows10.0.19041.0\MsixCore.OSAcceptance.dll'

# Tier 1: real package controls and MSIX Core repacks.
dotnet $tool pack 'C:\Users\andre\Downloads\CcProto_1.0.2.0_x64_Debug_Test\CcProto_1.0.2.0_x64_Debug_Test\Dependencies\x64\Microsoft.WindowsAppRuntime.1.8-experimental3.msix' C:\osaccept-work\war-x64
dotnet $tool pack 'C:\Users\andre\Downloads\CcProto_1.0.2.0_x64_Debug_Test\CcProto_1.0.2.0_x64_Debug_Test\CcProto_1.0.2.0_x64_Debug.msix' C:\osaccept-work\ccproto

foreach ($v in 'baseline','zip64','descriptor','both','no-utf8') {
  dotnet $tool variant C:\osaccept-work\war-x64\ours-optimal.msix C:\osaccept-work\war-x64\variant-$v.msix $v
  dotnet $tool variant C:\osaccept-work\ccproto\ours-optimal.msix C:\osaccept-work\ccproto\variant-$v.msix $v
}

foreach ($p in @(
  'C:\osaccept-work\war-x64\makeappx.msix','C:\osaccept-work\war-x64\ours-stored.msix','C:\osaccept-work\war-x64\ours-optimal.msix',
  'C:\osaccept-work\ccproto\makeappx.msix','C:\osaccept-work\ccproto\ours-stored.msix','C:\osaccept-work\ccproto\ours-optimal.msix')) {
  dotnet $tool read $p
}

# Tier 2 corrected CcProto install example; first verify CcProto is not pre-installed.
Get-AppxPackage -Name '*CcProto*'

# If absent, sign the CcProto real-identity variants with a fresh CN=Fruit cert,
# stage them under $env:USERPROFILE\osaccept, verify with signtool /pa, and run
# Add-AppxPackage against makeappx, baseline, and both.  If present, rewrite to a
# throwaway MsixCoreOSTest identity before installing.
```

## Cleanup performed

- Removed all packages matching `MsixCoreOSTest*`; final `Get-AppxPackage -Name 'MsixCoreOSTest*'` returned no packages.
- Removed all packages matching `fruitybunny.CcProto`; final `Get-AppxPackage -Name 'fruitybunny.CcProto'` returned no packages.
- Removed the test certificates `C04748A55D9B1BD8FE5FB361EB7B7D82FFD13A86`, `157125F316332997849C3587836AFB8C06FE4D93`, `6C2D03BF8F5DD16DEB434082ED80BBE4BC7B8A85`, and `59383FECA334C2A578FE24DF14C8D0FE8D788361` from CurrentUser stores touched during testing.
- Deleted scratch directories `C:\osaccept-work` and `%USERPROFILE%\osaccept` after recording the results above.
- No package bytes or payloads are committed.
