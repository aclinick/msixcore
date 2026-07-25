#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Compares msixmgr pack/unpack performance with the Windows SDK MakeAppx tool.

.DESCRIPTION
    Generates deterministic MSIX source layouts, builds msixmgr in Release, performs
    one discarded warmup plus repeated measured runs, validates cross-tool
    interoperability, and writes Markdown and JSON result files under bench.
    Both tools are launched as external processes. Peak working set is read from the
    process object; private bytes is sampled every five milliseconds.

.EXAMPLE
    pwsh bench\Compare-Tools.ps1 -Iterations 7
#>
[CmdletBinding()]
param(
    [ValidateRange(3, 100)]
    [int]$Iterations = 7,
    [string]$MakeAppxPath = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe',
    [string]$Configuration = 'Release',
    [string]$OutputPath = (Join-Path $PSScriptRoot 'comparison-results.md'),
    [switch]$KeepArtifacts,
    [switch]$SkipBuild,
    [switch]$UseSpecifiedMakeAppx
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent $PSScriptRoot
$workRoot = Join-Path $PSScriptRoot 'results'
$corpusRoot = Join-Path $workRoot 'corpora'
$packageRoot = Join-Path $workRoot 'packages'
$extractRoot = Join-Path $workRoot 'extract'
$jsonPath = [IO.Path]::ChangeExtension($OutputPath, '.json')
$project = Join-Path $repoRoot 'src\msixmgr\msixmgr.csproj'
$cli = Join-Path $repoRoot "src\msixmgr\bin\$Configuration\net10.0\msixmgr.exe"

function Format-Size([double]$bytes) {
    if ($bytes -ge 1GB) { return ('{0:N2} GB' -f ($bytes / 1GB)) }
    if ($bytes -ge 1MB) { return ('{0:N2} MB' -f ($bytes / 1MB)) }
    if ($bytes -ge 1KB) { return ('{0:N2} KB' -f ($bytes / 1KB)) }
    return ('{0:N0} B' -f $bytes)
}

function Quote-Markdown([string]$value) {
    return $value.Replace('|', '\|')
}

function Get-PercentileMedian([double[]]$values) {
    $ordered = @($values | Sort-Object)
    $count = $ordered.Count
    if (($count % 2) -eq 1) { return [double]$ordered[[int]($count / 2)] }
    return ([double]$ordered[$count / 2 - 1] + [double]$ordered[$count / 2]) / 2
}

function New-DeterministicFile {
    param([string]$Path, [long]$Length, [int]$Seed)

    $parent = Split-Path -Parent $Path
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    $random = [Random]::new($Seed)
    $buffer = [byte[]]::new(1MB)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $remaining = $Length
        while ($remaining -gt 0) {
            $count = [int][Math]::Min($buffer.Length, $remaining)
            $random.NextBytes($buffer)
            $stream.Write($buffer, 0, $count)
            $remaining -= $count
        }
    }
    finally {
        $stream.Dispose()
    }
}

function New-SolidPng {
    param([string]$Path, [int]$Width, [int]$Height, [int]$Argb)

    Add-Type -AssemblyName System.Drawing.Common
    $bitmap = [Drawing.Bitmap]::new($Width, $Height)
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.Color]::FromArgb($Argb))
        }
        finally {
            $graphics.Dispose()
        }
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

function New-Corpus {
    param(
        [string]$Name,
        [int]$FileCount,
        [long]$PayloadBytes
    )

    $root = Join-Path $corpusRoot $Name
    [IO.Directory]::CreateDirectory($root) | Out-Null
    $identityName = "MsixCore.Comparison.$Name"
    $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
         xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
         IgnorableNamespaces="rescap">
  <Identity Name="$identityName" Publisher="CN=MsixCoreBench" Version="1.0.0.0" ProcessorArchitecture="arm64" />
  <Properties>
    <DisplayName>MSIX Core Comparison $Name</DisplayName>
    <PublisherDisplayName>MSIX Core Bench</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>
  <Resources>
    <Resource Language="en-us" />
  </Resources>
  <Applications>
    <Application Id="App" Executable="App\App.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="Comparison $Name" Description="MSIX benchmark payload"
          BackgroundColor="#000000" Square150x150Logo="Assets\Square150.png"
          Square44x44Logo="Assets\Square44.png" />
    </Application>
  </Applications>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
"@
    [IO.File]::WriteAllText((Join-Path $root 'AppxManifest.xml'), $manifest, [Text.UTF8Encoding]::new($false))

    [IO.Directory]::CreateDirectory((Join-Path $root 'Assets')) | Out-Null
    New-SolidPng -Path (Join-Path $root 'Assets\StoreLogo.png') -Width 50 -Height 50 -Argb 0xff0078d4
    New-SolidPng -Path (Join-Path $root 'Assets\Square150.png') -Width 150 -Height 150 -Argb 0xff0078d4
    New-SolidPng -Path (Join-Path $root 'Assets\Square44.png') -Width 44 -Height 44 -Argb 0xff0078d4
    $logoBytes = (Get-ChildItem (Join-Path $root 'Assets') -File | Measure-Object Length -Sum).Sum

    $required = @(
        @{ Path = 'App\App.exe'; Seed = 1001 }
    )
    $allPayload = @($required)
    for ($i = 4; $i -lt $FileCount; $i++) {
        $allPayload += @{ Path = "Payload\file$($i.ToString('D4')).bin"; Seed = 2000 + $i }
    }

    $randomPayloadBytes = $PayloadBytes - $logoBytes
    $baseLength = [long]($randomPayloadBytes / $allPayload.Count)
    $written = 0L
    for ($i = 0; $i -lt $allPayload.Count; $i++) {
        $length = if ($i -eq ($allPayload.Count - 1)) { $randomPayloadBytes - $written } else { $baseLength }
        New-DeterministicFile -Path (Join-Path $root $allPayload[$i].Path) -Length $length -Seed $allPayload[$i].Seed
        $written += $length
    }

    return [pscustomobject]@{
        Name = $Name
        Root = $root
        FileCount = $FileCount
        PayloadBytes = $PayloadBytes
    }
}

function Invoke-MeasuredProcess {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$Label
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $ArgumentList) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $timer = [Diagnostics.Stopwatch]::StartNew()
    if (-not $process.Start()) { throw "Failed to start $Label." }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()

    $privatePeak = 0L
    $workingSetPeak = 0L
    $sdkModules = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $sample = 0
    while (-not $process.WaitForExit(5)) {
        try {
            $process.Refresh()
            $privatePeak = [Math]::Max($privatePeak, $process.PrivateMemorySize64)
            $workingSetPeak = [Math]::Max($workingSetPeak, $process.WorkingSet64)
            $workingSetPeak = [Math]::Max($workingSetPeak, $process.PeakWorkingSet64)
            if ($sample++ -eq 0) {
                foreach ($module in $process.Modules) {
                    [void]$sdkModules.Add($module.FileName)
                }
            }
        }
        catch [InvalidOperationException] {
            # The process may exit between WaitForExit and sampling.
        }
        catch [ComponentModel.Win32Exception] {
            # Module enumeration may be unavailable across architectures.
        }
    }
    $timer.Stop()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $process.Refresh()
    $privatePeak = [Math]::Max($privatePeak, $process.PrivateMemorySize64)
    $workingSetPeak = [Math]::Max($workingSetPeak, $process.PeakWorkingSet64)
    $exitCode = $process.ExitCode
    $process.Dispose()

    if ($exitCode -ne 0) {
        throw "$Label failed with exit code $exitCode.`nstdout:`n$stdout`nstderr:`n$stderr"
    }

    return [pscustomobject]@{
        ElapsedMs = $timer.Elapsed.TotalMilliseconds
        PeakWorkingSetBytes = [long]$workingSetPeak
        SampledPeakPrivateBytes = [long]$privatePeak
        Modules = @($sdkModules)
        StdOut = $stdout
        StdErr = $stderr
    }
}

function Remove-Output([string]$Path) {
    if (Test-Path $Path) { Remove-Item -Recurse -Force $Path }
}

function Invoke-OperationRuns {
    param(
        [string]$Operation,
        [pscustomobject]$Corpus,
        [string]$Tool,
        [scriptblock]$Prepare,
        [string]$FilePath,
        [scriptblock]$Arguments
    )

    $runs = @()
    for ($run = 0; $run -le $Iterations; $run++) {
        & $Prepare $run
        $args = & $Arguments $run
        $measurement = Invoke-MeasuredProcess -FilePath $FilePath -ArgumentList $args `
            -Label "$Tool $Operation $($Corpus.Name), run $run"
        if ($run -eq 0) {
            Write-Host "  warmup: $Tool $Operation $($Corpus.Name) $($measurement.ElapsedMs.ToString('N2')) ms"
        }
        else {
            $runs += [pscustomobject]@{
                Operation = $Operation
                Corpus = $Corpus.Name
                PayloadBytes = $Corpus.PayloadBytes
                Tool = $Tool
                Run = $run
                ElapsedMs = $measurement.ElapsedMs
                PeakWorkingSetBytes = $measurement.PeakWorkingSetBytes
                SampledPeakPrivateBytes = $measurement.SampledPeakPrivateBytes
            }
            foreach ($module in $measurement.Modules) { [void]$script:observedMakeAppxModules.Add($module) }
        }
    }
    return $runs
}

function Assert-SourceFilesMatch {
    param([string]$Source, [string]$Extracted)
    foreach ($file in Get-ChildItem -Recurse -File $Source) {
        $relative = [IO.Path]::GetRelativePath($Source, $file.FullName)
        $other = Join-Path $Extracted $relative
        if (-not (Test-Path $other -PathType Leaf)) { throw "Round-trip omitted '$relative'." }
        if ((Get-FileHash $file.FullName -Algorithm SHA256).Hash -ne
            (Get-FileHash $other -Algorithm SHA256).Hash) {
            throw "Round-trip content mismatch for '$relative'."
        }
    }
}

if (-not (Test-Path $MakeAppxPath -PathType Leaf)) {
    throw "MakeAppx was not found at '$MakeAppxPath'. Install the Windows SDK or pass -MakeAppxPath <path>."
}

$requestedMakeAppxPath = (Resolve-Path $MakeAppxPath).Path
$hostArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
$selectedMakeAppxPath = $requestedMakeAppxPath
$usedNativeAlternative = $false
if (-not $UseSpecifiedMakeAppx -and $hostArchitecture -eq 'Arm64') {
    $nativeCandidate = $requestedMakeAppxPath -replace '\\x64\\makeappx\.exe$', '\arm64\makeappx.exe'
    if ($nativeCandidate -ne $requestedMakeAppxPath -and (Test-Path $nativeCandidate -PathType Leaf)) {
        $selectedMakeAppxPath = (Resolve-Path $nativeCandidate).Path
        $usedNativeAlternative = $true
    }
}

Write-Host "MakeAppx requested: $requestedMakeAppxPath"
Write-Host "MakeAppx measured : $selectedMakeAppxPath"
if ($usedNativeAlternative) {
    Write-Host 'Using the native Arm64 SDK binary; pass -UseSpecifiedMakeAppx to benchmark x64 emulation.'
}

if (-not $SkipBuild) {
    & dotnet build $project -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Release build of msixmgr failed.' }
}
if (-not (Test-Path $cli -PathType Leaf)) {
    throw "Cannot find the built CLI at '$cli'. Run without -SkipBuild."
}

Remove-Output $workRoot
[IO.Directory]::CreateDirectory($corpusRoot) | Out-Null
[IO.Directory]::CreateDirectory($packageRoot) | Out-Null
[IO.Directory]::CreateDirectory($extractRoot) | Out-Null

$corpora = @(
    New-Corpus -Name 'small-1MiB-8files' -FileCount 8 -PayloadBytes 1MB
    New-Corpus -Name 'medium-10MiB-64files' -FileCount 64 -PayloadBytes 10MB
    New-Corpus -Name 'large-64MiB-128files' -FileCount 128 -PayloadBytes 64MB
)

$script:observedMakeAppxModules = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$rawResults = @()

foreach ($corpus in $corpora) {
    Write-Host "Preparing canonical package: $($corpus.Name)" -ForegroundColor Cyan
    $canonical = Join-Path $packageRoot "$($corpus.Name)-canonical.msix"
    [void](Invoke-MeasuredProcess -FilePath $selectedMakeAppxPath `
        -ArgumentList @('pack', '/d', $corpus.Root, '/p', $canonical, '/o', '/nc') `
        -Label "MakeAppx canonical pack $($corpus.Name)")
    $ourPack = Join-Path $packageRoot "$($corpus.Name)-msixmgr.msix"
    $sdkPack = Join-Path $packageRoot "$($corpus.Name)-makeappx.msix"

    Write-Host "Benchmarking pack: $($corpus.Name)" -ForegroundColor Cyan
    $rawResults += Invoke-OperationRuns -Operation 'Pack' -Corpus $corpus -Tool 'msixmgr' `
        -Prepare { param($run) Remove-Output $ourPack } -FilePath $cli `
        -Arguments { param($run) @('pack', $corpus.Root, '-o', $ourPack, '--overwrite') }
    $rawResults += Invoke-OperationRuns -Operation 'Pack' -Corpus $corpus -Tool 'MakeAppx' `
        -Prepare { param($run) Remove-Output $sdkPack } -FilePath $selectedMakeAppxPath `
        -Arguments { param($run) @('pack', '/d', $corpus.Root, '/p', $sdkPack, '/o', '/nc') }

    # Produce stable packages after the timed runs and verify cross-tool consumption.
    Remove-Output $ourPack
    [void](Invoke-MeasuredProcess -FilePath $cli `
        -ArgumentList @('pack', $corpus.Root, '-o', $ourPack, '--overwrite') `
        -Label "msixmgr interoperability pack $($corpus.Name)")
    $ourExtract = Join-Path $extractRoot "$($corpus.Name)-our-by-sdk"
    Remove-Output $ourExtract
    [void](Invoke-MeasuredProcess -FilePath $selectedMakeAppxPath `
        -ArgumentList @('unpack', '/p', $ourPack, '/d', $ourExtract, '/o') `
        -Label "MakeAppx unpack of msixmgr package $($corpus.Name)")
    Assert-SourceFilesMatch -Source $corpus.Root -Extracted $ourExtract

    [void](Invoke-MeasuredProcess -FilePath $cli `
        -ArgumentList @('inspect', $sdkPack, '--json') `
        -Label "msixmgr inspect of MakeAppx package $($corpus.Name)")

    Write-Host "Benchmarking unpack: $($corpus.Name)" -ForegroundColor Cyan
    $ourUnpack = Join-Path $extractRoot "$($corpus.Name)-msixmgr-timed"
    $sdkUnpack = Join-Path $extractRoot "$($corpus.Name)-makeappx-timed"
    $rawResults += Invoke-OperationRuns -Operation 'Unpack' -Corpus $corpus -Tool 'msixmgr' `
        -Prepare { param($run) Remove-Output $ourUnpack } -FilePath $cli `
        -Arguments { param($run) @('unpack', $canonical, '-Destination', $ourUnpack) }
    $rawResults += Invoke-OperationRuns -Operation 'Unpack' -Corpus $corpus -Tool 'MakeAppx' `
        -Prepare { param($run) Remove-Output $sdkUnpack } -FilePath $selectedMakeAppxPath `
        -Arguments { param($run) @('unpack', '/p', $canonical, '/d', $sdkUnpack, '/o') }

    Assert-SourceFilesMatch -Source $corpus.Root -Extracted $ourUnpack
    Assert-SourceFilesMatch -Source $corpus.Root -Extracted $sdkUnpack

    Write-Host "Benchmarking validate (msixmgr only): $($corpus.Name)" -ForegroundColor Cyan
    $rawResults += Invoke-OperationRuns -Operation 'Validate' -Corpus $corpus -Tool 'msixmgr' `
        -Prepare { param($run) } -FilePath $cli `
        -Arguments { param($run) @('validate', $canonical) }
}

$summaries = @()
foreach ($group in $rawResults | Group-Object Operation, Corpus, Tool) {
    $rows = @($group.Group)
    $summaries += [pscustomobject]@{
        Operation = $rows[0].Operation
        Corpus = $rows[0].Corpus
        PayloadBytes = $rows[0].PayloadBytes
        Tool = $rows[0].Tool
        MedianMs = Get-PercentileMedian @($rows.ElapsedMs)
        MinMs = ($rows.ElapsedMs | Measure-Object -Minimum).Minimum
        MaxMs = ($rows.ElapsedMs | Measure-Object -Maximum).Maximum
        MedianPeakWorkingSetBytes = Get-PercentileMedian @($rows.PeakWorkingSetBytes)
        MedianSampledPeakPrivateBytes = Get-PercentileMedian @($rows.SampledPeakPrivateBytes)
    }
}

$makeAppxDirectory = Split-Path -Parent $selectedMakeAppxPath
$sdkModules = @($observedMakeAppxModules |
    Where-Object { (Split-Path -Parent $_) -eq $makeAppxDirectory } |
    Sort-Object -Unique |
    ForEach-Object { Get-Item $_ })
if (-not ($sdkModules.FullName -contains $selectedMakeAppxPath)) {
    $sdkModules += Get-Item $selectedMakeAppxPath
}
$sdkFootprint = ($sdkModules | Measure-Object Length -Sum).Sum

$metadata = [ordered]@{
    Generated = (Get-Date).ToString('o')
    Iterations = $Iterations
    WarmupRunsDiscarded = 1
    HostOS = [Runtime.InteropServices.RuntimeInformation]::OSDescription.Trim()
    HostArchitecture = $hostArchitecture
    DotNetSdk = (& dotnet --version)
    CliPath = $cli
    RequestedMakeAppxPath = $requestedMakeAppxPath
    MeasuredMakeAppxPath = $selectedMakeAppxPath
    MakeAppxVersion = (Get-Item $selectedMakeAppxPath).VersionInfo.FileVersion
    UsedNativeAlternative = $usedNativeAlternative
    MakeAppxSdkLocalFootprintBytes = $sdkFootprint
    MakeAppxSdkLocalFiles = @($sdkModules | ForEach-Object {
        [ordered]@{ Name = $_.Name; Bytes = $_.Length }
    })
}

$json = [ordered]@{
    Metadata = $metadata
    Corpora = $corpora
    Summary = $summaries
    RawRuns = $rawResults
}
[IO.Directory]::CreateDirectory((Split-Path -Parent $OutputPath)) | Out-Null
$json | ConvertTo-Json -Depth 8 | Set-Content -Path $jsonPath -Encoding utf8

$sb = [Text.StringBuilder]::new()
[void]$sb.AppendLine('# SDK tool comparison — generated results')
[void]$sb.AppendLine()
[void]$sb.AppendLine("Generated: $($metadata.Generated)")
[void]$sb.AppendLine()
[void]$sb.AppendLine("- Host: ``$($metadata.HostOS)`` ($hostArchitecture)")
[void]$sb.AppendLine("- .NET SDK: ``$($metadata.DotNetSdk)``")
[void]$sb.AppendLine("- MakeAppx: ``$(Quote-Markdown $selectedMakeAppxPath)`` ($($metadata.MakeAppxVersion))")
[void]$sb.AppendLine("- Repetitions: $Iterations measured after one discarded warmup")
[void]$sb.AppendLine("- Packages are unsigned and stored/uncompressed (MakeAppx ``/nc``), matching msixmgr's current authoring mode.")
[void]$sb.AppendLine("- Ratio is **msixmgr / MakeAppx**; below 1.00 means msixmgr used less time or memory.")
[void]$sb.AppendLine()

foreach ($operation in @('Pack', 'Unpack')) {
    [void]$sb.AppendLine("## $operation")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('| Corpus | msixmgr time median [min–max] ms | MakeAppx time median [min–max] ms | Time ratio | msixmgr peak WS | MakeAppx peak WS | WS ratio |')
    [void]$sb.AppendLine('| --- | ---: | ---: | ---: | ---: | ---: | ---: |')
    foreach ($corpus in $corpora) {
        $our = $summaries | Where-Object { $_.Operation -eq $operation -and $_.Corpus -eq $corpus.Name -and $_.Tool -eq 'msixmgr' }
        $sdk = $summaries | Where-Object { $_.Operation -eq $operation -and $_.Corpus -eq $corpus.Name -and $_.Tool -eq 'MakeAppx' }
        [void]$sb.AppendLine(('| {0} ({1}) | {2:N2} [{3:N2}–{4:N2}] | {5:N2} [{6:N2}–{7:N2}] | {8:N2}x | {9} | {10} | {11:N2}x |' -f
            $corpus.Name, (Format-Size $corpus.PayloadBytes),
            $our.MedianMs, $our.MinMs, $our.MaxMs,
            $sdk.MedianMs, $sdk.MinMs, $sdk.MaxMs,
            ($our.MedianMs / $sdk.MedianMs),
            (Format-Size $our.MedianPeakWorkingSetBytes),
            (Format-Size $sdk.MedianPeakWorkingSetBytes),
            ($our.MedianPeakWorkingSetBytes / $sdk.MedianPeakWorkingSetBytes)))
    }
    [void]$sb.AppendLine()
}

[void]$sb.AppendLine('## Validate (no MakeAppx equivalent)')
[void]$sb.AppendLine()
[void]$sb.AppendLine('MakeAppx has no standalone block-map verification verb, so these msixmgr results are reported without a ratio rather than forcing a misleading comparison.')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| Corpus | msixmgr time median [min–max] ms | Peak working set |')
[void]$sb.AppendLine('| --- | ---: | ---: |')
foreach ($corpus in $corpora) {
    $our = $summaries | Where-Object { $_.Operation -eq 'Validate' -and $_.Corpus -eq $corpus.Name -and $_.Tool -eq 'msixmgr' }
    [void]$sb.AppendLine(('| {0} ({1}) | {2:N2} [{3:N2}–{4:N2}] | {5} |' -f
        $corpus.Name, (Format-Size $corpus.PayloadBytes),
        $our.MedianMs, $our.MinMs, $our.MaxMs,
        (Format-Size $our.MedianPeakWorkingSetBytes)))
}
[void]$sb.AppendLine()

[void]$sb.AppendLine('## Sampled peak private bytes')
[void]$sb.AppendLine()
[void]$sb.AppendLine('Private bytes are sampled every 5 ms, so short-lived peaks can be missed; peak working set above uses the OS-reported process peak sampled while the process is alive.')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| Operation | Corpus | msixmgr | MakeAppx | Ratio |')
[void]$sb.AppendLine('| --- | --- | ---: | ---: | ---: |')
foreach ($operation in @('Pack', 'Unpack')) {
    foreach ($corpus in $corpora) {
        $our = $summaries | Where-Object { $_.Operation -eq $operation -and $_.Corpus -eq $corpus.Name -and $_.Tool -eq 'msixmgr' }
        $sdk = $summaries | Where-Object { $_.Operation -eq $operation -and $_.Corpus -eq $corpus.Name -and $_.Tool -eq 'MakeAppx' }
        [void]$sb.AppendLine(('| {0} | {1} | {2} | {3} | {4:N2}x |' -f
            $operation, $corpus.Name,
            (Format-Size $our.MedianSampledPeakPrivateBytes),
            (Format-Size $sdk.MedianSampledPeakPrivateBytes),
            ($our.MedianSampledPeakPrivateBytes / $sdk.MedianSampledPeakPrivateBytes)))
    }
}
[void]$sb.AppendLine()
[void]$sb.AppendLine('## Observed MakeAppx SDK-local footprint')
[void]$sb.AppendLine()
[void]$sb.AppendLine("| Total | Files |")
[void]$sb.AppendLine("| ---: | --- |")
[void]$sb.AppendLine("| $(Format-Size $sdkFootprint) | $(($sdkModules.Name -join ', ')) |")
[void]$sb.AppendLine()
[void]$sb.AppendLine('Cross-tool checks passed: every msixmgr package unpacked with MakeAppx and matched its source files; every MakeAppx package opened with `msixmgr inspect`; both tools reproduced every source file from the canonical packages.')

[IO.File]::WriteAllText($OutputPath, $sb.ToString(), [Text.UTF8Encoding]::new($false))
Write-Host "`nWrote $OutputPath and $jsonPath" -ForegroundColor Green
Get-Content $OutputPath | Write-Host

if (-not $KeepArtifacts) {
    Remove-Output $workRoot
}
