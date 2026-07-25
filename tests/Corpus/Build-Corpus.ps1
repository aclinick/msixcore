<#
.SYNOPSIS
    Regenerates the MSIX Core feature-test corpus: loose (unpacked) fixtures, hand-built
    AppxBlockMap.xml for each, packed .msix packages (via makeappx), an .msixbundle, an
    optionally signed package, and the machine-readable test matrix corpus.json.

.DESCRIPTION
    Every fixture exercises a distinct MSIX feature/manifest surface (architectures, schema
    namespaces, capabilities, extensions, VFS, package kinds, block-map edge cases, display
    metadata, signing, bundles). Expected parsed values written to corpus.json are derived
    *independently* of the MsixCore library (via System.Xml + an independent MSIX publisher-hash
    implementation), so the corpus is a genuine differential oracle for the library.

    With -RunOracle, each loose fixture is validated against the real Windows implementation
    (Add-AppxPackage -Register under Developer Mode); the verdict is recorded and the package is
    always removed afterwards. No packages or certificates are left installed.

.PARAMETER RunOracle
    Register each loose fixture with Windows and record the accept/reject verdict.

.PARAMETER Sign
    Sign packages flagged for signing with a throwaway self-signed cert (removed afterwards).
#>
[CmdletBinding()]
param(
    [switch]$RunOracle,
    [switch]$Sign,
    [string]$Publisher = 'CN=MsixCoreCorpus',
    [string]$SignThumbprint = '1999384EEF0362515797C62766388F94B46EA7A7'
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression | Out-Null

$CorpusRoot   = $PSScriptRoot
$FixturesRoot = Join-Path $CorpusRoot 'fixtures'
$PackedRoot   = Join-Path $CorpusRoot 'packed'

# ---- Tooling discovery ------------------------------------------------------
function Resolve-Kit([string]$exe) {
    Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter $exe -ErrorAction SilentlyContinue |
        Where-Object FullName -like '*\x64\*' |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
$MakeAppx = Resolve-Kit 'makeappx.exe'
$SignTool = Resolve-Kit 'signtool.exe'
if (-not $MakeAppx) { throw 'makeappx.exe not found under the Windows SDK.' }
$WinApp = (Get-Command winapp -ErrorAction SilentlyContinue).Source

# ---- Signing certificate (trusted self-signed cert from the current user) ----
# The repo owner keeps a self-signed code-signing cert (subject == the user) in the
# Trusted People store, so packages signed with it install via Add-AppxPackage.
# We export it to a throwaway PFX for winapp and delete the PFX afterwards; we never
# modify any certificate store.
$SignPfx = $null
$SignPfxPassword = 'corpus'
if ($Sign) {
    if (-not $WinApp) {
        throw '-Sign was requested but the winapp CLI (used for signing) was not found on PATH.'
    }
    $signCert = Get-Item "Cert:\CurrentUser\My\$SignThumbprint" -ErrorAction SilentlyContinue
    if (-not $signCert -or -not $signCert.HasPrivateKey) {
        throw "Signing cert $SignThumbprint with a private key was not found in Cert:\CurrentUser\My."
    }
    # Honour the cert's exact subject DN as the manifest Publisher.
    $Publisher = $signCert.Subject
    $SignPfx = Join-Path $CorpusRoot '_corpus_sign.pfx'
    $sp = ConvertTo-SecureString $SignPfxPassword -AsPlainText -Force
    Export-PfxCertificate -Cert $signCert -FilePath $SignPfx -Password $sp -ChainOption EndEntityCertOnly | Out-Null
}

function Invoke-SignPackage([string]$path) {
    if (-not $Sign -or -not $SignPfx) { return $false }
    & $WinApp sign $path $SignPfx --password $SignPfxPassword --quiet 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "winapp sign failed (exit $LASTEXITCODE) for '$path'."
    }
    return $true
}

# ---- Independent MSIX helpers (do NOT use the library under test) -----------
$Base32 = '0123456789abcdefghjkmnpqrstvwxyz'
function Get-PublisherHash([string]$publisher) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $digest = $sha.ComputeHash([System.Text.Encoding]::Unicode.GetBytes($publisher))
    $bits = ''
    for ($i = 0; $i -lt 8; $i++) { $bits += [Convert]::ToString($digest[$i], 2).PadLeft(8, '0') }
    $bits += '0'
    $out = ''
    for ($g = 0; $g -lt 13; $g++) { $out += $Base32[[Convert]::ToInt32($bits.Substring($g * 5, 5), 2)] }
    return $out
}
$PubHash = Get-PublisherHash $Publisher

function Get-ArchMoniker([string]$arch) {
    switch ($arch) { 'x64' { 'x64' } 'x86' { 'x86' } 'arm64' { 'arm64' } 'arm' { 'arm' } default { 'neutral' } }
}
function Get-ArchEnum([string]$arch) {
    switch ($arch) { 'x64' { 'X64' } 'x86' { 'X86' } 'arm64' { 'Arm64' } 'arm' { 'Arm' } 'x86a64' { 'X86OnArm64' } default { 'Neutral' } }
}

# A minimal valid 1x1 PNG, reused for every logo/asset.
$PngBytes = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==')

function New-BlockMapXml([string]$dir) {
    # Serializes an AppxBlockMap.xml matching every file under $dir except AppxBlockMap.xml itself.
    # 64 KiB uncompressed blocks, SHA-256 base64 per block (mirrors the library's own block map).
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.Append('<?xml version="1.0" encoding="UTF-8"?>')
    [void]$sb.Append('<BlockMap xmlns="http://schemas.microsoft.com/appx/2010/blockmap" ')
    [void]$sb.Append('HashMethod="http://www.w3.org/2001/04/xmlenc#sha256">')
    $files = Get-ChildItem -Path $dir -Recurse -File | Where-Object { $_.Name -ne 'AppxBlockMap.xml' } | Sort-Object FullName
    foreach ($f in $files) {
        $rel = $f.FullName.Substring($dir.Length).TrimStart('\', '/').Replace('/', '\')
        $bytes = [IO.File]::ReadAllBytes($f.FullName)
        [void]$sb.Append('<File Name="').Append([System.Security.SecurityElement]::Escape($rel)).Append('" Size="').Append($bytes.Length).Append('" LfhSize="0">')
        for ($off = 0; $off -lt $bytes.Length; $off += 65536) {
            $len = [Math]::Min(65536, $bytes.Length - $off)
            $chunk = New-Object byte[] $len
            [Array]::Copy($bytes, $off, $chunk, 0, $len)
            $hash = [Convert]::ToBase64String($sha.ComputeHash($chunk))
            [void]$sb.Append('<Block Hash="').Append($hash).Append('" />')
        }
        [void]$sb.Append('</File>')
    }
    [void]$sb.Append('</BlockMap>')
    return $sb.ToString()
}

# ---- OPC package builder (self-built ZIP; avoids makeappx manifest validation) ----
function Get-OpcSegment([string]$seg) {
    # Percent-encode the OPC-reserved characters makeappx encodes in ZIP part names.
    $sb = [System.Text.StringBuilder]::new()
    foreach ($ch in $seg.ToCharArray()) {
        if ($ch -in ' ', '!', '+', '#', '%', '{', '}', '^', '`', '@', '&') {
            [void]$sb.Append(('%{0:X2}' -f [int][char]$ch))
        }
        else { [void]$sb.Append($ch) }
    }
    return $sb.ToString()
}

$ContentTypesXml = @'
<?xml version="1.0" encoding="UTF-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="xml" ContentType="application/vnd.ms-appx.manifest+xml" />
  <Default Extension="png" ContentType="image/png" />
  <Default Extension="exe" ContentType="application/octet-stream" />
  <Default Extension="dll" ContentType="application/octet-stream" />
  <Default Extension="txt" ContentType="text/plain" />
  <Default Extension="bin" ContentType="application/octet-stream" />
  <Default Extension="dat" ContentType="application/octet-stream" />
</Types>
'@

function Add-ZipText($zip, [string]$name, [string]$text) {
    $entry = $zip.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
    $s = $entry.Open()
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($text)
    $s.Write($bytes, 0, $bytes.Length)
    $s.Dispose()
}

function New-OpcPackage([string]$srcDir, [string]$outPath, [string]$blockMapXml) {
    if (Test-Path $outPath) { Remove-Item -Force $outPath }
    $fs = [System.IO.File]::Open($outPath, [System.IO.FileMode]::Create)
    $zip = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        $files = Get-ChildItem -Path $srcDir -Recurse -File | Where-Object { $_.Name -ne 'AppxBlockMap.xml' } | Sort-Object FullName
        foreach ($f in $files) {
            $rel = $f.FullName.Substring($srcDir.Length).TrimStart('\', '/').Replace('\', '/')
            $entryName = ($rel -split '/' | ForEach-Object { Get-OpcSegment $_ }) -join '/'
            $entry = $zip.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
            $es = $entry.Open()
            $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
            if ($bytes.Length -gt 0) { $es.Write($bytes, 0, $bytes.Length) }
            $es.Dispose()
        }
        Add-ZipText $zip '[Content_Types].xml' $ContentTypesXml
        Add-ZipText $zip 'AppxBlockMap.xml' $blockMapXml
    }
    finally {
        $zip.Dispose()
        $fs.Dispose()
    }
}

# ---- Manifest builder -------------------------------------------------------
function New-Manifest($f) {
    $archAttr = if ($f.Arch) { " ProcessorArchitecture=`"$($f.Arch)`"" } else { '' }
    $resAttr  = if ($f.ResourceId) { " ResourceId=`"$($f.ResourceId)`"" } else { '' }
    $ns   = if ($f.ContainsKey('ExtraNs')) { $f.ExtraNs } else { '' }
    $ign  = 'uap rescap' + $(if ($f.ContainsKey('IgnExtra')) { ' ' + $f.IgnExtra } else { '' })
    $props = if ($f.ContainsKey('PropsExtra')) { "`n    " + $f.PropsExtra } else { '' }
    $deps  = if ($f.ContainsKey('DepsExtra')) { "`n    " + $f.DepsExtra } else { '' }
    if ($f.ContainsKey('CapsXml')) { $caps = $f.CapsXml }
    else { $caps = "`n  <Capabilities>`n    <rescap:Capability Name=`"runFullTrust`" />`n  </Capabilities>" }

    if ($f.ContainsKey('Apps')) {
        $apps = $f.Apps
    }
    elseif ($f.ContainsKey('IncludeApp') -and -not $f.IncludeApp) {
        $apps = ''
    }
    else {
        $appExt = if ($f.ContainsKey('AppExt')) { $f.AppExt } else { '' }
        $short = $f.Display
        if ($short.Length -gt 40) { $short = $short.Substring(0, 40) }
        $apps = @"

  <Applications>
    <Application Id="App" Executable="app.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="$($f.Display)" Description="MSIX Core corpus fixture" BackgroundColor="transparent" Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png">
        <uap:DefaultTile ShortName="$short" />
      </uap:VisualElements>$appExt
    </Application>
  </Applications>
"@
    }

    return @"
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"$ns
  IgnorableNamespaces="$ign">
  <Identity Name="$($f.Name)" Publisher="$Publisher" Version="$($f.Version)"$archAttr$resAttr />
  <Properties>
    <DisplayName>$($f.Display)</DisplayName>
    <PublisherDisplayName>MSIX Core Corpus</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>$props
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.22621.0" />$deps
  </Dependencies>
  <Resources>
    <Resource Language="en-us" />
  </Resources>$caps$apps
</Package>
"@
}

# ---- Independent expected-value extraction (via System.Xml) -----------------
function Get-LocalChild($node, [string]$name) {
    if ($null -eq $node) { return $null }
    foreach ($c in $node.ChildNodes) {
        if ($c.NodeType -eq 'Element' -and $c.LocalName -eq $name) { return $c }
    }
    return $null
}

function Get-Expected($manifestPath, $arch) {
    [xml]$doc = Get-Content -Raw -Path $manifestPath
    $pkg = $doc.DocumentElement
    $id  = Get-LocalChild $pkg 'Identity'
    $name = $id.GetAttribute('Name')
    $ver  = $id.GetAttribute('Version')
    $resId = $id.GetAttribute('ResourceId')

    $propsEl = Get-LocalChild $pkg 'Properties'
    $displayEl = Get-LocalChild $propsEl 'DisplayName'
    $pubDisplayEl = Get-LocalChild $propsEl 'PublisherDisplayName'
    $display = if ($displayEl) { $displayEl.InnerText.Trim() } else { '' }
    $pubDisplay = if ($pubDisplayEl) { $pubDisplayEl.InnerText.Trim() } else { '' }
    $isFramework = $false
    $fwEl = Get-LocalChild $propsEl 'Framework'
    if ($fwEl) { $isFramework = $fwEl.InnerText.Trim() -in @('true', '1') }

    $caps = New-Object System.Collections.Generic.List[string]
    $seen = New-Object System.Collections.Generic.HashSet[string]
    $capsEl = Get-LocalChild $pkg 'Capabilities'
    if ($capsEl) {
        foreach ($c in $capsEl.ChildNodes) {
            if ($c.NodeType -ne 'Element') { continue }
            $cn = $c.GetAttribute('Name')
            if ($cn -and $seen.Add($cn)) { [void]$caps.Add($cn) }
        }
    }

    $appCount = 0
    $appsEl = Get-LocalChild $pkg 'Applications'
    if ($appsEl) {
        foreach ($a in $appsEl.ChildNodes) {
            if ($a.NodeType -eq 'Element' -and $a.LocalName -eq 'Application') { $appCount++ }
        }
    }

    $moniker = Get-ArchMoniker $arch
    return [ordered]@{
        name                = $name
        publisher           = $Publisher
        version             = $ver
        architecture        = Get-ArchEnum $arch
        resourceId          = $resId
        packageFamilyName   = "${name}_$PubHash"
        packageFullName     = "${name}_${ver}_${moniker}_${resId}_$PubHash"
        displayName         = $display
        publisherDisplayName = $pubDisplay
        capabilities        = $caps.ToArray()
        isFramework         = $isFramework
        applicationCount    = $appCount
    }
}

# ---- Payload writer ---------------------------------------------------------
function Write-Fixture($f) {
    $dir = Join-Path $FixturesRoot $f.Id
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null

    # Manifest
    $manifest = New-Manifest $f
    [IO.File]::WriteAllText((Join-Path $dir 'AppxManifest.xml'), $manifest, [System.Text.UTF8Encoding]::new($false))

    # Assets (logos)
    $wantAssets = -not ($f.ContainsKey('IncludeAssets') -and -not $f.IncludeAssets)
    if ($wantAssets) {
        New-Item -ItemType Directory -Force -Path (Join-Path $dir 'Assets') | Out-Null
        foreach ($n in 'StoreLogo.png', 'Square150x150Logo.png', 'Square44x44Logo.png', 'Wide310x150Logo.png') {
            [IO.File]::WriteAllBytes((Join-Path $dir "Assets\$n"), $PngBytes)
        }
    }

    # app.exe
    $wantExe = -not ($f.ContainsKey('IncludeAppExe') -and -not $f.IncludeAppExe)
    if ($wantExe) { [IO.File]::WriteAllBytes((Join-Path $dir 'app.exe'), [byte[]](0x4D, 0x5A, 0x90, 0x00)) }

    # Extra payload files
    if ($f.ContainsKey('Payload')) {
        foreach ($p in $f.Payload) {
            $full = Join-Path $dir $p.Path
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $full) | Out-Null
            [IO.File]::WriteAllBytes($full, $p.Bytes)
        }
    }
    return $dir
}

# =============================================================================
#  Fixture definitions
# =============================================================================
$B = [byte[]](0x4D, 0x5A, 0x90, 0x00)   # tiny dummy payload
function Bytes([string]$s) { [System.Text.Encoding]::UTF8.GetBytes($s) }

$fixtures = @(
    # ---- Architectures ----
    @{ Id = 'arch-x64';     Features = @('architecture:x64');     Name = 'MsixCoreCorpus.ArchX64';  Arch = 'x64';     Version = '1.0.0.0'; Display = 'Corpus Arch x64' }
    @{ Id = 'arch-x86';     Features = @('architecture:x86');     Name = 'MsixCoreCorpus.ArchX86';  Arch = 'x86';     Version = '1.0.0.0'; Display = 'Corpus Arch x86' }
    @{ Id = 'arch-arm64';   Features = @('architecture:arm64');   Name = 'MsixCoreCorpus.ArchArm64'; Arch = 'arm64';  Version = '1.0.0.0'; Display = 'Corpus Arch arm64' }
    @{ Id = 'arch-neutral'; Features = @('architecture:neutral'); Name = 'MsixCoreCorpus.ArchNeutral'; Arch = 'neutral'; Version = '1.0.0.0'; Display = 'Corpus Arch neutral'; IncludeApp = $false; IncludeAppExe = $false; CapsXml = '' }

    # ---- Capabilities ----
    @{ Id = 'cap-general';    Features = @('capabilities:general', 'namespace:uap'); Name = 'MsixCoreCorpus.CapGeneral'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus General Caps'
       CapsXml = "`n  <Capabilities>`n    <Capability Name=`"internetClient`" />`n    <Capability Name=`"privateNetworkClientServer`" />`n    <rescap:Capability Name=`"runFullTrust`" />`n  </Capabilities>" }
    @{ Id = 'cap-restricted'; Features = @('capabilities:restricted', 'namespace:rescap'); Name = 'MsixCoreCorpus.CapRestricted'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus Restricted Caps'
       CapsXml = "`n  <Capabilities>`n    <rescap:Capability Name=`"runFullTrust`" />`n    <rescap:Capability Name=`"broadFileSystemAccess`" />`n  </Capabilities>" }
    @{ Id = 'cap-device';     Features = @('capabilities:device'); Name = 'MsixCoreCorpus.CapDevice'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus Device Caps'
       CapsXml = "`n  <Capabilities>`n    <rescap:Capability Name=`"runFullTrust`" />`n    <DeviceCapability Name=`"webcam`" />`n    <DeviceCapability Name=`"microphone`" />`n  </Capabilities>" }

    # ---- Extensions (schema namespaces) ----
    @{ Id = 'ext-fileassoc'; Features = @('extension:fileTypeAssociation', 'namespace:uap'); Name = 'MsixCoreCorpus.ExtFileAssoc'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus File Assoc'
       AppExt = @"

      <Extensions>
        <uap:Extension Category="windows.fileTypeAssociation">
          <uap:FileTypeAssociation Name="corpusdoc">
            <uap:DisplayName>Corpus Document</uap:DisplayName>
            <uap:SupportedFileTypes>
              <uap:FileType>.corpusdoc</uap:FileType>
            </uap:SupportedFileTypes>
          </uap:FileTypeAssociation>
        </uap:Extension>
      </Extensions>
"@ }
    @{ Id = 'ext-protocol'; Features = @('extension:protocol', 'namespace:uap'); Name = 'MsixCoreCorpus.ExtProtocol'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus Protocol'
       AppExt = @"

      <Extensions>
        <uap:Extension Category="windows.protocol">
          <uap:Protocol Name="corpus-scheme">
            <uap:DisplayName>Corpus Scheme</uap:DisplayName>
          </uap:Protocol>
        </uap:Extension>
      </Extensions>
"@ }
    @{ Id = 'ext-execalias'; Features = @('extension:appExecutionAlias', 'namespace:uap3', 'namespace:desktop'); Name = 'MsixCoreCorpus.ExtExecAlias'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus Exec Alias'
       ExtraNs = "`n  xmlns:uap3=`"http://schemas.microsoft.com/appx/manifest/uap/windows10/3`"`n  xmlns:desktop=`"http://schemas.microsoft.com/appx/manifest/desktop/windows10`""; IgnExtra = 'uap3 desktop'
       AppExt = @"

      <Extensions>
        <uap3:Extension Category="windows.appExecutionAlias">
          <uap3:AppExecutionAlias>
            <desktop:ExecutionAlias Alias="corpusapp.exe" />
          </uap3:AppExecutionAlias>
        </uap3:Extension>
      </Extensions>
"@ }
    @{ Id = 'ext-startuptask'; Features = @('extension:startupTask', 'namespace:desktop'); Name = 'MsixCoreCorpus.ExtStartupTask'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus Startup Task'
       ExtraNs = "`n  xmlns:desktop=`"http://schemas.microsoft.com/appx/manifest/desktop/windows10`""; IgnExtra = 'desktop'
       AppExt = @"

      <Extensions>
        <desktop:Extension Category="windows.startupTask" Executable="app.exe" EntryPoint="Windows.FullTrustApplication">
          <desktop:StartupTask TaskId="CorpusStartup" Enabled="true" DisplayName="Corpus Startup" />
        </desktop:Extension>
      </Extensions>
"@ }
    @{ Id = 'ext-com'; Features = @('extension:comServer', 'namespace:com'); Name = 'MsixCoreCorpus.ExtCom'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus COM Server'
       ExtraNs = "`n  xmlns:com=`"http://schemas.microsoft.com/appx/manifest/com/windows10`""; IgnExtra = 'com'
       AppExt = @"

      <Extensions>
        <com:Extension Category="windows.comServer">
          <com:ComServer>
            <com:ExeServer Executable="app.exe" DisplayName="Corpus COM">
              <com:Class Id="00000000-1111-2222-3333-444444444444" />
            </com:ExeServer>
          </com:ComServer>
        </com:Extension>
      </Extensions>
"@ }
    @{ Id = 'ext-appservice'; Features = @('extension:appService', 'namespace:uap'); Name = 'MsixCoreCorpus.ExtAppService'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus App Service'
       AppExt = @"

      <Extensions>
        <uap:Extension Category="windows.appService" EntryPoint="MsixCoreCorpus.AppService">
          <uap:AppService Name="com.msixcorecorpus.service" />
        </uap:Extension>
      </Extensions>
"@ }
    @{ Id = 'ext-bgtask'; Features = @('extension:backgroundTasks'); Name = 'MsixCoreCorpus.ExtBgTask'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus Background Task'
       AppExt = @"

      <Extensions>
        <Extension Category="windows.backgroundTasks" Executable="app.exe" EntryPoint="Corpus.BgTask">
          <BackgroundTasks>
            <Task Type="general" />
          </BackgroundTasks>
        </Extension>
      </Extensions>
"@ }
    @{ Id = 'ext-sharetarget'; Features = @('extension:shareTarget', 'namespace:uap'); Name = 'MsixCoreCorpus.ExtShareTarget'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus Share Target'
       AppExt = @"

      <Extensions>
        <uap:Extension Category="windows.shareTarget">
          <uap:ShareTarget>
            <uap:SupportedFileTypes>
              <uap:FileType>.corpusdoc</uap:FileType>
            </uap:SupportedFileTypes>
            <uap:DataFormat>Text</uap:DataFormat>
          </uap:ShareTarget>
        </uap:Extension>
      </Extensions>
"@ }
    @{ Id = 'ext-contextmenu'; Features = @('extension:fileExplorerContextMenus', 'namespace:desktop4', 'namespace:com'); Name = 'MsixCoreCorpus.ExtContextMenu'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus Context Menu'
       ExtraNs = "`n  xmlns:com=`"http://schemas.microsoft.com/appx/manifest/com/windows10`"`n  xmlns:desktop4=`"http://schemas.microsoft.com/appx/manifest/desktop/windows10/4`"`n  xmlns:desktop5=`"http://schemas.microsoft.com/appx/manifest/desktop/windows10/5`""; IgnExtra = 'com desktop4 desktop5'
       AppExt = @"

      <Extensions>
        <com:Extension Category="windows.comServer">
          <com:ComServer>
            <com:SurrogateServer DisplayName="Corpus Context Menu">
              <com:Class Id="00000000-aaaa-bbbb-cccc-555555555555" Path="app.dll" ThreadingModel="STA" />
            </com:SurrogateServer>
          </com:ComServer>
        </com:Extension>
        <desktop4:Extension Category="windows.fileExplorerContextMenus">
          <desktop4:FileExplorerContextMenus>
            <desktop4:ItemType Type="*">
              <desktop4:Verb Id="Corpus" Clsid="00000000-aaaa-bbbb-cccc-555555555555" />
            </desktop4:ItemType>
          </desktop4:FileExplorerContextMenus>
        </desktop4:Extension>
      </Extensions>
"@ }

    # ---- VFS content ----
    @{ Id = 'vfs-content'; Features = @('vfs'); Name = 'MsixCoreCorpus.Vfs'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus VFS'
       Payload = @(
           @{ Path = 'VFS\ProgramFilesX64\CorpusApp\readme.txt'; Bytes = (Bytes 'program files payload') }
           @{ Path = 'VFS\SystemX64\corpus.dll'; Bytes = $B }
           @{ Path = 'VFS\AppVPackageDrive\CorpusApp\data.bin'; Bytes = $B }
       ) }

    # ---- Package kinds ----
    @{ Id = 'kind-framework'; Features = @('kind:framework'); Name = 'MsixCoreCorpus.Framework'; Arch = 'x64'; Version = '2.1.0.0'; Display = 'Corpus Framework'
       IncludeApp = $false; IncludeAppExe = $false; CapsXml = ''
       PropsExtra = '<Framework>true</Framework>'
       OracleExpect = 'expected-not-installable'; OracleReason = 'Framework packages cannot be registered via Add-AppxPackage -Register in DevelopmentMode; Windows installs them only as dependencies of apps.' }
    @{ Id = 'kind-optional'; Features = @('kind:optional'); Name = 'MsixCoreCorpus.Optional'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus Optional'
       ExtraNs = "`n  xmlns:uap3=`"http://schemas.microsoft.com/appx/manifest/uap/windows10/3`""; IgnExtra = 'uap3'
       IncludeApp = $false; IncludeAppExe = $false; CapsXml = ''
       DepsExtra = '<uap3:MainPackageDependency Name="MsixCoreCorpus.ArchX64" />'
       OracleExpect = 'expected-not-installable'; OracleReason = 'Optional package requires its main package to be installed first.' }
    @{ Id = 'kind-modification'; Features = @('kind:modification'); Name = 'MsixCoreCorpus.Modification'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus Modification'
       ExtraNs = "`n  xmlns:uap4=`"http://schemas.microsoft.com/appx/manifest/uap/windows10/4`""; IgnExtra = 'uap4'
       IncludeApp = $false; IncludeAppExe = $false; CapsXml = ''
       DepsExtra = '<uap4:MainPackageDependency Name="MsixCoreCorpus.ArchX64" Publisher="CN=MsixCoreCorpus" />'
       OracleExpect = 'expected-not-installable'; OracleReason = 'Modification package requires its host (main) package to be installed first.' }
    @{ Id = 'kind-sparse'; Features = @('kind:sparse', 'namespace:uap10'); Name = 'MsixCoreCorpus.Sparse'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus Sparse'
       ExtraNs = "`n  xmlns:uap10=`"http://schemas.microsoft.com/appx/manifest/uap/windows10/10`""; IgnExtra = 'uap10'
       PropsExtra = '<uap10:AllowExternalContent>true</uap10:AllowExternalContent>'
       OracleExpect = 'expected-not-installable'; OracleReason = 'Sparse (external-content) package requires -ExternalLocation to register.' }
    @{ Id = 'kind-resource'; Features = @('kind:resource'); Name = 'MsixCoreCorpus.Resource'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus Resource'; ResourceId = 'en-US'
       IncludeApp = $false; IncludeAppExe = $false; CapsXml = ''
       OracleExpect = 'expected-not-installable'; OracleReason = 'Resource package (ResourceId set) is only installable alongside its main package.' }

    # ---- Block map edge cases ----
    @{ Id = 'blockmap-empty'; Features = @('blockmap:emptyFile'); Name = 'MsixCoreCorpus.BlockEmpty'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus Empty File'
       Payload = @(@{ Path = 'empty.dat'; Bytes = ([byte[]]@()) }) }
    @{ Id = 'blockmap-multiblock'; Features = @('blockmap:multiBlock'); Name = 'MsixCoreCorpus.BlockMulti'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus Multi Block'
       Payload = @(@{ Path = 'big.bin'; Bytes = (New-Object byte[] 200000) }) }
    @{ Id = 'blockmap-percentname'; Features = @('blockmap:percentEncodedNames', 'issue:7'); Name = 'MsixCoreCorpus.BlockPercent'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus Percent Names'
       Payload = @(
           @{ Path = 'hello world.txt'; Bytes = (Bytes 'space in name') }
           @{ Path = 'a+b.txt'; Bytes = (Bytes 'plus in name') }
           @{ Path = 'bang!.txt'; Bytes = (Bytes 'bang in name') }
       )
       PackedBlockMapValid = $true
       SelfBuild = $true }
    @{ Id = 'blockmap-deepnested'; Features = @('blockmap:deepNesting'); Name = 'MsixCoreCorpus.BlockDeep'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus Deep Nested'
       Payload = @(@{ Path = 'a\b\c\d\e\f\g\deep.txt'; Bytes = (Bytes 'deep') }) }

    # ---- Display metadata ----
    @{ Id = 'meta-multiapp'; Features = @('metadata:multipleApplications'); Name = 'MsixCoreCorpus.MultiApp'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus Multi App'
       Apps = @"

  <Applications>
    <Application Id="App1" Executable="app.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="Corpus App One" Description="First app" BackgroundColor="transparent" Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png">
        <uap:DefaultTile ShortName="App One" />
      </uap:VisualElements>
    </Application>
    <Application Id="App2" Executable="app.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="Corpus App Two" Description="Second app" BackgroundColor="transparent" Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png">
        <uap:DefaultTile ShortName="App Two" />
      </uap:VisualElements>
    </Application>
  </Applications>
"@ }
    @{ Id = 'meta-logos'; Features = @('metadata:logosAndDescription'); Name = 'MsixCoreCorpus.Logos'; Arch = 'x64'; Version = '3.2.1.0'; Display = 'Corpus Logos'
       PropsExtra = '<Description>A corpus fixture exercising logos and description metadata.</Description>'
       AppExt = @"

      <uap:VisualElements DisplayName="Corpus Logos" Description="MSIX Core corpus fixture" BackgroundColor="#0078D7" Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png">
"@ }

    # ---- Signing ----
    @{ Id = 'signed-basic'; Features = @('signing:signed'); Name = 'MsixCoreCorpus.Signed'; Arch = 'x64'; Version = '1.0.0.0'; Display = 'Corpus Signed'; SignPackage = $true }
)

# meta-logos AppExt above accidentally duplicates VisualElements; fix by using explicit Apps instead.
$fixtures = $fixtures | ForEach-Object {
    if ($_.Id -eq 'meta-logos') {
        $_.Remove('AppExt')
        $_['Apps'] = @"

  <Applications>
    <Application Id="App" Executable="app.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="Corpus Logos" Description="Logos and description fixture" BackgroundColor="#0078D7" Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png">
        <uap:DefaultTile Wide310x150Logo="Assets\Wide310x150Logo.png" ShortName="Logos" />
      </uap:VisualElements>
    </Application>
  </Applications>
"@
    }
    $_
}

# =============================================================================
#  Generation
# =============================================================================
try {
New-Item -ItemType Directory -Force -Path $FixturesRoot | Out-Null
New-Item -ItemType Directory -Force -Path $PackedRoot | Out-Null

$records = New-Object System.Collections.Generic.List[object]

foreach ($f in $fixtures) {
    Write-Host "==> Fixture $($f.Id)" -ForegroundColor Cyan
    $looseDir = Write-Fixture $f

    # Generate the block map (covers manifest + payload); shared by loose and packed.
    $blockMap = New-BlockMapXml $looseDir
    $blockMapFileCount = (Get-ChildItem $looseDir -Recurse -File | Where-Object { $_.Name -ne 'AppxBlockMap.xml' }).Count

    # Pack the package. makeappx produces a signtool-recognized APPX (so it can be signed
    # and installed as a real .msix); fall back to a self-built OPC ZIP when makeappx rejects
    # the manifest or when the fixture needs percent-encoded part names (reproduces issue #7).
    $packedRel = $null
    $packedPath = $null
    $blockMapValidPacked = $null
    $packedSelfBuilt = $false
    $doPack = -not ($f.ContainsKey('Pack') -and -not $f.Pack)
    if ($doPack) {
        $packedPath = Join-Path $PackedRoot "$($f.Id).msix"
        $forceSelfBuild = $f.ContainsKey('SelfBuild') -and $f.SelfBuild
        $packedOk = $false
        if (-not $forceSelfBuild -and $MakeAppx) {
            & $MakeAppx pack /o /nv /d $looseDir /p $packedPath 2>&1 | Out-Null
            $packedOk = ($LASTEXITCODE -eq 0)
        }
        if (-not $packedOk) {
            New-OpcPackage $looseDir $packedPath $blockMap
            $packedSelfBuilt = $true
        }
        $packedRel = "packed/$($f.Id).msix"
        if ($f.ContainsKey('PackedBlockMapValid')) { $blockMapValidPacked = [bool]$f.PackedBlockMapValid }
        else { $blockMapValidPacked = $true }
    }

    # Sign makeappx-produced packages with the trusted corpus cert (winapp) when -Sign is set.
    # Self-built OPC ZIPs are not recognized by signtool, so they remain unsigned.
    $isSignedPacked = $false
    if ($doPack -and $packedRel -and -not $packedSelfBuilt) {
        $isSignedPacked = Invoke-SignPackage $packedPath
    }

    # Write the loose block map into the loose dir.
    [IO.File]::WriteAllText((Join-Path $looseDir 'AppxBlockMap.xml'), $blockMap, [System.Text.UTF8Encoding]::new($false))

    $expected = Get-Expected (Join-Path $looseDir 'AppxManifest.xml') $f.Arch

    $rec = [ordered]@{
        id                  = $f.Id
        features            = $f.Features
        kind                = 'package'
        looseDir            = "fixtures/$($f.Id)"
        packedFile          = $packedRel
        expectedSupported   = $true
        windowsOracle       = [ordered]@{ verdict = 'not-attempted'; reason = '' }
        expected            = $expected
        isSignedLoose       = $false
        isSignedPacked      = $isSignedPacked
        blockMapFileCount   = $blockMapFileCount
        blockMapValidLoose  = $true
        blockMapValidPacked = $blockMapValidPacked
        packedKnownBug      = $(if ($f.ContainsKey('PackedBug')) { $f.PackedBug } else { $null })
        notes               = $(if ($f.ContainsKey('Notes')) { $f.Notes } else { '' })
        _oracleExpect       = $(if ($f.ContainsKey('OracleExpect')) { $f.OracleExpect } else { 'installed' })
        _oracleReason       = $(if ($f.ContainsKey('OracleReason')) { $f.OracleReason } else { '' })
        _oracleRegister     = -not ($f.ContainsKey('OracleRegister') -and -not $f.OracleRegister)
    }
    $records.Add($rec)
}

# =============================================================================
#  Bundle (built from two packed architecture packages)
# =============================================================================
$x64Msix = Join-Path $PackedRoot 'arch-x64.msix'
$x86Msix = Join-Path $PackedRoot 'arch-x86.msix'
$bundlePath = Join-Path $PackedRoot 'bundle-multiarch.msixbundle'
$bundleManifest = @"
<?xml version="1.0" encoding="UTF-8"?>
<Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle" SchemaVersion="1.0">
  <Identity Name="MsixCoreCorpus.Bundle" Publisher="$Publisher" Version="1.0.0.0" />
  <Packages>
    <Package Type="application" Version="1.0.0.0" Architecture="x64" FileName="arch-x64.msix" Offset="0" Size="$((Get-Item $x64Msix).Length)">
      <Resources><Resource Language="en-us" /></Resources>
    </Package>
    <Package Type="application" Version="1.0.0.0" Architecture="x86" FileName="arch-x86.msix" Offset="0" Size="$((Get-Item $x86Msix).Length)">
      <Resources><Resource Language="en-us" /></Resources>
    </Package>
  </Packages>
</Bundle>
"@
$bundleContentTypes = @'
<?xml version="1.0" encoding="UTF-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="msix" ContentType="application/octet-stream" />
  <Default Extension="xml" ContentType="application/vnd.ms-appx.bundlemanifest+xml" />
</Types>
'@
if (Test-Path $bundlePath) { Remove-Item -Force $bundlePath }
$bfs = [System.IO.File]::Open($bundlePath, [System.IO.FileMode]::Create)
$bzip = New-Object System.IO.Compression.ZipArchive($bfs, [System.IO.Compression.ZipArchiveMode]::Create, $false)
try {
    foreach ($pkg in 'arch-x64.msix', 'arch-x86.msix') {
        $e = $bzip.CreateEntry($pkg, [System.IO.Compression.CompressionLevel]::NoCompression)
        $s = $e.Open(); $b = [System.IO.File]::ReadAllBytes((Join-Path $PackedRoot $pkg)); $s.Write($b, 0, $b.Length); $s.Dispose()
    }
    Add-ZipText $bzip 'AppxMetadata/AppxBundleManifest.xml' $bundleManifest
    Add-ZipText $bzip '[Content_Types].xml' $bundleContentTypes
}
finally { $bzip.Dispose(); $bfs.Dispose() }

# Sign the bundle with the trusted corpus cert (winapp) when -Sign is set.
$bundleSigned = $false
if ($Sign) { $bundleSigned = Invoke-SignPackage $bundlePath }

$records.Add([ordered]@{
    id                  = 'bundle-multiarch'
    features            = @('bundle:multiArch')
    kind                = 'bundle'
    looseDir            = $null
    packedFile          = 'packed/bundle-multiarch.msixbundle'
    expectedSupported   = $false
    windowsOracle       = [ordered]@{ verdict = 'not-attempted'; reason = 'Bundle applicability is not implemented in the reader; a fixture is provided for future phases.' }
    expected            = $null
    isSignedLoose       = $false
    isSignedPacked      = $bundleSigned
    blockMapFileCount   = $null
    blockMapValidLoose  = $null
    blockMapValidPacked = $null
    packedKnownBug      = $null
    notes               = 'Bundle applicability is not implemented in the reader; opening its manifest as an app manifest throws InvalidDataException (documented current behavior).'
    _oracleExpect       = 'not-attempted'
    _oracleReason       = ''
    _oracleRegister     = $false
})

# =============================================================================
#  Windows oracle (loose registration under Developer Mode)
# =============================================================================
if ($RunOracle) {
    foreach ($rec in $records) {
        if ($rec.kind -eq 'bundle') { continue }
        if (-not $rec._oracleRegister -and $rec._oracleExpect -ne 'expected-not-installable') { continue }
        $pkgName = $rec.expected.name
        $packedFull = if ($rec.packedFile) { Join-Path $CorpusRoot ($rec.packedFile.Replace('/', '\')) } else { $null }
        $looseManifest = if ($rec.looseDir) { Join-Path $CorpusRoot ($rec.looseDir.Replace('/', '\') + '\AppxManifest.xml') } else { $null }
        # Prefer installing the trusted-signed packed .msix; fall back to loose registration.
        $useSignedPacked = $rec.isSignedPacked -and $packedFull -and (Test-Path $packedFull)
        $mode = if ($useSignedPacked) { 'signed package' } else { 'loose manifest' }
        Write-Host "--> Oracle $($rec.id) ($mode)" -ForegroundColor DarkYellow
        try {
            if ($useSignedPacked) { Add-AppxPackage -Path $packedFull -ErrorAction Stop }
            elseif ($looseManifest -and (Test-Path $looseManifest)) { Add-AppxPackage -Register $looseManifest -ErrorAction Stop }
            else { continue }
            $installed = Get-AppxPackage -Name $pkgName
            if ($installed) {
                $rec.windowsOracle.verdict = 'installed'
                $rec.windowsOracle.reason = "Windows accepted the $mode; PackageFullName=$($installed.PackageFullName)"
            }
        }
        catch {
            $msg = $_.Exception.Message -replace '\s+', ' '
            if ($msg.Length -gt 300) { $msg = $msg.Substring(0, 300) }
            if ($rec._oracleExpect -eq 'expected-not-installable') {
                $rec.windowsOracle.verdict = 'expected-not-installable'
                $rec.windowsOracle.reason = "$($rec._oracleReason) Windows: $msg"
            }
            else {
                $rec.windowsOracle.verdict = 'failed'
                $rec.windowsOracle.reason = $msg
            }
        }
        finally {
            Get-AppxPackage -Name $pkgName | Remove-AppxPackage -ErrorAction SilentlyContinue
        }
    }
    # Safety net: remove anything left behind.
    Get-AppxPackage -Name 'MsixCoreCorpus*' | Remove-AppxPackage -ErrorAction SilentlyContinue
}

# Remove the throwaway signing PFX (the certificate store is left untouched). Done in a finally
# below so an interrupted or failed run never leaves the exported private key on disk.

# =============================================================================
#  Emit corpus.json (strip internal helper fields)
# =============================================================================
$clean = foreach ($rec in $records) {
    $copy = [ordered]@{}
    foreach ($k in $rec.Keys) { if (-not $k.StartsWith('_')) { $copy[$k] = $rec[$k] } }
    [pscustomobject]$copy
}
$meta = [ordered]@{
    generator     = 'tests/Corpus/Build-Corpus.ps1'
    publisher     = $Publisher
    publisherHash = $PubHash
    makeappx      = $MakeAppx
    generatedUtc  = (Get-Date).ToUniversalTime().ToString('o')
    fixtureCount  = $clean.Count
}
$out = [ordered]@{ meta = $meta; fixtures = $clean }
$json = $out | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText((Join-Path $CorpusRoot 'corpus.json'), $json, [System.Text.UTF8Encoding]::new($false))

Write-Host ""
Write-Host "Corpus generated: $($clean.Count) fixtures -> $(Join-Path $CorpusRoot 'corpus.json')" -ForegroundColor Green
if ($RunOracle) {
    Write-Host "Oracle verdicts:" -ForegroundColor Green
    $records | ForEach-Object { '  {0,-22} {1}' -f $_.id, $_.windowsOracle.verdict } | Write-Host
}
}
finally {
    if ($SignPfx -and (Test-Path $SignPfx)) { Remove-Item $SignPfx -Force -ErrorAction SilentlyContinue }
}
