# Windows OS AppX acceptance of MSIX Core-authored packages

Date: 2026-07-25.  Machine: Windows_NT, Windows SDK makeappx/signtool 10.0.26100.0.  Worktree: `D:\source\msixcore-osaccept`, branch `feature/os-acceptance`.

## Bottom line

The Windows OS AppxPackaging COM reader **does accept packages authored by MSIX Core (.NET)**.  For two real packages repacked from normalized source, both MSIX Core `Stored` and `Optimal` outputs opened successfully, exposed the manifest identity, enumerated payload files, and returned the block map.

For the OS reader, **ZIP64 is not required, data descriptors are not required, and the UTF-8 general-purpose bit is tolerated**.  The ablation matrix also shows the reader accepts ZIP64, data descriptors, both together, and clearing the UTF-8 bit.

Full `Add-AppxPackage` registration was attempted only with throwaway identities.  It did not reach a successful install in this non-elevated environment: unsigned packages were rejected by unsigned-package policy, and signed packages were rejected because AppX deployment did not trust the current-user test root (`0x800B0109`) even though `signtool verify /pa` trusted it.  No install failure implicated ZIP64, data descriptors, UTF-8, block map, or MSIX Core ZIP structure.

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
- C / `descriptor`: baseline plus GP bit 3, zero local CRC/sizes, `PK\x07\x08` data descriptor with 8-byte sizes after data.
- D / `both`: B + C.
- E / `no-utf8`: baseline with GP bit `0x0800` cleared.

| Case | A baseline | B ZIP64 | C descriptors | D both | E no UTF-8 |
|---|---|---|---|---|---|
| `war-x64` reader | OK | OK | OK | OK | OK |
| `ccproto` reader | OK | OK | OK | OK | OK |

All cells returned exit code 0 from the OS reader helper; no HRESULT/error was emitted.

## Tier 2 install attempts and exact failures

Safety: every install attempt used a throwaway identity beginning with `MsixCoreOSTest`; no real package identity was installed or removed.

### Attempt 1: signed framework package, publisher `CN=MsixCoreOSTest`

Package: normalized WindowsAppRuntime package rewritten to `Name=MsixCoreOSTest.WindowsAppRuntime18Experimental3`, signed with a 7-day self-signed code-signing cert subject `CN=MsixCoreOSTest`.  The cert thumbprint was `C04748A55D9B1BD8FE5FB361EB7B7D82FFD13A86`; it was imported to CurrentUser `TrustedPeople` and CurrentUser `Root` only.

`signtool verify /pa /v C:\osaccept-work\install-signed\variant-baseline.msix` succeeded.  `Add-AppxPackage` still failed for baseline/ZIP64/both/no-UTF8 with trust error:

- Cmdlet exception wrapper: `HResult=0x80070002`
- Deployment HRESULT: `0x80073CF0` (`Package could not be opened`)
- Specific error: `0x800B0109: The root certificate of the signature in the app package or bundle must be trusted.`
- AppXDeploymentServer events included `[402] error 0x87E80034: Reading manifest ... failed with error: Unknown error` and `[404] ... error 0x80073CF0`.

The signed descriptor-only package failed earlier in the reader/deployment path after signing:

- OS reader: `COM failure HRESULT=0x80511007`
- Add-AppxPackage specific error: `Common::Deployment::MsixvcStagingSession::GetManifestReader ... failed with error 0x87E80034`.

Because signing adds `AppxSignature.p7x`, this descriptor-only signed failure is not evidence that unsigned descriptor-only ZIPs are unreadable; the unsigned descriptor-only package was accepted by the OS reader before signing.

### Attempt 2: `Add-AppxPackage -AllowUnsigned`, framework package with unsigned OID

Package: WindowsAppRuntime rewritten to `Name=MsixCoreOSTest.Unsigned.WAR18`, `Publisher=CN=MsixCoreOSTest, OID.2.25.311729368913984317654407730594956997722=1`.

All variants failed identically by unsigned-package policy after the package identity was read:

- Cmdlet exception wrapper: `HResult=0x80131500`
- Deployment HRESULT: `0x80073D2B`
- Event 798: `Windows cannot install package MsixCoreOSTest.Unsigned.WAR18_8000.526.1808.0_x64__cb2j79ga0v7n2 because an unsigned package must be a Main package.`

### Attempt 3: `Add-AppxPackage -AllowUnsigned`, CcProto main package with unsigned OID

Package: CcProto rewritten to `Name=MsixCoreOSTest.Unsigned.CcProto`, same unsigned OID publisher.

All variants failed identically by unsigned-package policy after the package identity was read:

- Cmdlet exception wrapper: `HResult=0x80131500`
- Deployment HRESULT: `0x80073D2B`
- Event 796: `Windows cannot install package MsixCoreOSTest.Unsigned.CcProto because an unsigned package cannot include Executable activations.`

### Attempt 4: minimal UWP web-content package with unsigned OID

A scratch package with `StartPage=index.html` and no executable field was authored in both makeappx and MSIX Core forms.  The OS reader accepted makeappx, MSIX Core Stored, MSIX Core Optimal, and all five ZIP variants.  `Add-AppxPackage -AllowUnsigned` rejected all forms identically:

- Cmdlet exception wrapper: `HResult=0x80131500`
- Deployment HRESULT: `0x80073D2B`
- Event 791: `Windows cannot install package MsixCoreOSTest.UwpContent_1.0.0.0_x64__cb2j79ga0v7n2 because it is not a valid unsigned package.`

## Recommendation

1. **Do not change `StoredZipWriter` for ZIP64 or data descriptors as a correctness fix.**  The real OS reader accepted MSIX Core-authored packages without either feature, and accepted the ablation variants with either or both features present.
2. **Treat makeappx's always-ZIP64 output as cosmetic for normal-size packages.**  Implement ZIP64 only when needed for >4 GiB offsets/sizes or >65,535 entries, or if a future compatibility target proves it necessary.
3. **Treat makeappx's data descriptors as cosmetic for reader compatibility.**  They are not required by the OS reader.  Descriptor-only packages should not be signed using the experimental post-processor without further investigation because the signed copy failed reader/deployment (`0x80511007` / `0x87E80034`).
4. **The UTF-8 bit is tolerated.**  Clearing it was also tolerated for these ASCII-only package paths, so there is no evidence that either setting is a correctness issue.
5. **Follow-up if full install proof is required:** run the same committed commands from an elevated shell, import the test certificate into the machine trust store, then repeat the signed `Add-AppxPackage` matrix.  The current non-elevated run could not alter machine trust and therefore could not complete signed installation.

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

# Tier 2 throwaway install example; all installed packages are removed immediately by name prefix.
dotnet $tool pack-throwaway 'C:\Users\andre\Downloads\CcProto_1.0.2.0_x64_Debug_Test\CcProto_1.0.2.0_x64_Debug_Test\Dependencies\x64\Microsoft.WindowsAppRuntime.1.8-experimental3.msix' C:\osaccept-work\install MsixCoreOSTest.WindowsAppRuntime18Experimental3 'CN=MsixCoreOSTest'
foreach ($v in 'baseline','zip64','descriptor','both','no-utf8') {
  dotnet $tool variant C:\osaccept-work\install\ours-optimal.msix C:\osaccept-work\install\variant-$v.msix $v
}
```

## Cleanup performed

- Removed all packages matching `MsixCoreOSTest*`; final `Get-AppxPackage -Name 'MsixCoreOSTest*'` returned no packages.
- Removed the test certificate `C04748A55D9B1BD8FE5FB361EB7B7D82FFD13A86` from CurrentUser `My`, `TrustedPeople`, and `Root`.
- Deleted scratch directory `C:\osaccept-work` after recording the results above.
- No package bytes or payloads are committed.
