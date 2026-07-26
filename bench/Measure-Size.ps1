#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Measures the published binary size of the msixkit CLI across deployment configurations.

.DESCRIPTION
    Publishes src/msixkit framework-dependent, self-contained, and trimmed for
    win-x64 and win-arm64. Trim failures remain non-fatal so the report explains
    unsupported configurations rather than losing the other measurements.

    For each configuration it records the total published output size and the size of the
    key assemblies, then writes a Markdown summary to bench/size-report.md.

    A comparison against the original C++ MSIX Core (msixkit.exe / MsixCore.dll) is a FUTURE
    step: those binaries are not part of this repository. The intended methodology is documented
    in the generated report.

.EXAMPLE
    pwsh bench/Measure-Size.ps1
#>
[CmdletBinding()]
param(
    [string[]]$Runtime = @('win-x64', 'win-arm64'),
    [string]$Configuration = 'Release',
    [string]$WindowsSdkBin = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src/msixkit/msixkit.csproj'
$publishRoot = Join-Path $PSScriptRoot 'publish'
$reportPath = Join-Path $PSScriptRoot 'size-report.md'
$hostArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()

if (-not (Test-Path $project)) {
    throw "Cannot find msixkit project at $project"
}

if (Test-Path $publishRoot) {
    Remove-Item -Recurse -Force $publishRoot
}

function Format-Size([long]$bytes) {
    if ($bytes -ge 1MB) { return ('{0:N2} MB' -f ($bytes / 1MB)) }
    if ($bytes -ge 1KB) { return ('{0:N2} KB' -f ($bytes / 1KB)) }
    return "$bytes B"
}

function Publish-Config {
    param(
        [string]$Name,
        [string]$OutDir,
        [string[]]$ExtraArgs
    )

    Write-Host "==> Publishing configuration: $Name" -ForegroundColor Cyan
    $args = @(
        'publish', $project,
        '-c', $Configuration,
        '-o', $OutDir,
        '--nologo'
    ) + $ExtraArgs

    & dotnet @args 2>&1 | Tee-Object -Variable output | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Configuration '$Name' failed to publish; skipping."
        $output | Select-Object -Last 15 | ForEach-Object { Write-Host $_ }
        return $null
    }

    $files = Get-ChildItem -Recurse -File $OutDir
    $total = ($files | Measure-Object -Property Length -Sum).Sum
    if ($null -eq $total) { $total = 0 }

    $exe = $files | Where-Object { $_.Name -in @('msixkit.exe', 'msixkit') } | Select-Object -First 1
    $keyAssemblies = $files |
        Where-Object { $_.Extension -in @('.dll', '.exe') -and $_.Name -like 'MsixCore*' -or $_.Name -like 'msixkit*' } |
        Sort-Object Length -Descending |
        Select-Object -First 6

    return [pscustomobject]@{
        Name          = $Name
        OutDir        = $OutDir
        TotalBytes    = [long]$total
        FileCount     = $files.Count
        ExeBytes      = if ($exe) { [long]$exe.Length } else { 0 }
        KeyAssemblies = $keyAssemblies
    }
}

$results = @()

$results += Publish-Config -Name 'Framework-dependent (portable, host architecture)' `
    -OutDir (Join-Path $publishRoot 'framework-dependent') `
    -ExtraArgs @('--self-contained', 'false')

foreach ($rid in $Runtime) {
    $results += Publish-Config -Name "Self-contained ($rid)" `
        -OutDir (Join-Path $publishRoot "self-contained-$rid") `
        -ExtraArgs @('-r', $rid, '--self-contained', 'true')

    $results += Publish-Config -Name "Self-contained + trimmed ($rid)" `
        -OutDir (Join-Path $publishRoot "self-contained-trimmed-$rid") `
        -ExtraArgs @('-r', $rid, '--self-contained', 'true', '-p:PublishTrimmed=true')
}

$results = $results | Where-Object { $null -ne $_ }

# --- Emit report -----------------------------------------------------------
$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine('# msixkit published-size report')
[void]$sb.AppendLine()
[void]$sb.AppendLine("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')")
[void]$sb.AppendLine()
[void]$sb.AppendLine("- Runtime identifiers: ``$($Runtime -join ', ')``")
[void]$sb.AppendLine("- Configuration: ``$Configuration``")
[void]$sb.AppendLine("- .NET SDK: ``$(dotnet --version)``")
[void]$sb.AppendLine("- Host OS: ``$([System.Runtime.InteropServices.RuntimeInformation]::OSDescription.Trim())``")
[void]$sb.AppendLine()

[void]$sb.AppendLine('## Totals')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| Configuration | Total size | Files | msixkit host |')
[void]$sb.AppendLine('| --- | ---: | ---: | ---: |')
foreach ($r in $results) {
    [void]$sb.AppendLine("| $($r.Name) | $(Format-Size $r.TotalBytes) | $($r.FileCount) | $(Format-Size $r.ExeBytes) |")
}
[void]$sb.AppendLine()

[void]$sb.AppendLine('## Windows SDK MakeAppx footprint')
[void]$sb.AppendLine()
[void]$sb.AppendLine('The total includes `makeappx.exe` plus the SDK-local DLLs observed loaded by the')
[void]$sb.AppendLine('comparison harness (`appxpackaging.dll` and `opcservices.dll`). OS DLLs are excluded.')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| SDK tool | Total size | Files |')
[void]$sb.AppendLine('| --- | ---: | --- |')
foreach ($arch in @('x64', 'arm64')) {
    $sdkFiles = @('makeappx.exe', 'appxpackaging.dll', 'opcservices.dll') |
        ForEach-Object { Join-Path (Join-Path $WindowsSdkBin $arch) $_ } |
        Where-Object { Test-Path $_ -PathType Leaf } |
        ForEach-Object { Get-Item $_ }
    if ($sdkFiles.Count -eq 3) {
        $sdkTotal = ($sdkFiles | Measure-Object Length -Sum).Sum
        $label = if ($arch -eq 'x64' -and $hostArchitecture -eq 'Arm64') {
            'MakeAppx SDK tool (x64 binary; emulated on this Arm64 host)'
        } elseif ($arch -eq 'x64') {
            'MakeAppx SDK tool (x64 native)'
        } else {
            "MakeAppx SDK tool (Arm64$(if ($hostArchitecture -eq 'Arm64') { ' native' } else { ' binary' }))"
        }
        [void]$sb.AppendLine("| $label | $(Format-Size $sdkTotal) | $(($sdkFiles.Name -join ', ')) |")
    }
}
[void]$sb.AppendLine()

[void]$sb.AppendLine('## Key assemblies (per configuration)')
[void]$sb.AppendLine()
foreach ($r in $results) {
    [void]$sb.AppendLine("### $($r.Name)")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('| Assembly | Size |')
    [void]$sb.AppendLine('| --- | ---: |')
    foreach ($a in $r.KeyAssemblies) {
        [void]$sb.AppendLine("| $($a.Name) | $(Format-Size $a.Length) |")
    }
    [void]$sb.AppendLine()
}

[void]$sb.AppendLine('## Comparison against the original C++ MSIX Core (future work)')
[void]$sb.AppendLine()
[void]$sb.AppendLine('The original C++ `msixkit.exe` and `MsixCore` binaries are **not** part of this')
[void]$sb.AppendLine('repository, so a direct size comparison cannot be produced here yet. Intended methodology:')
[void]$sb.AppendLine()
[void]$sb.AppendLine('1. Obtain an official release build of the C++ MSIX Core `msixkit.exe` (and its')
[void]$sb.AppendLine('   dependent DLLs) for `win-x64` from the upstream `microsoft/msix-packaging` project.')
[void]$sb.AppendLine('2. Record the on-disk size of the shipped executable + DLLs (the C++ build has no')
[void]$sb.AppendLine('   managed runtime, so its natural analogue is the **framework-dependent** column,')
[void]$sb.AppendLine('   while the **self-contained** column reflects the true "no prerequisites" install size).')
[void]$sb.AppendLine('3. Compare like-for-like: framework-dependent .NET vs. C++ needing the OS CRT/redist;')
[void]$sb.AppendLine('   self-contained/trimmed .NET vs. C++ statically linked, if available.')
[void]$sb.AppendLine('4. Track both totals and the "core packaging" binary size (`MsixCore.Packaging.dll`')
[void]$sb.AppendLine('   vs. the C++ `msix.dll`) over time in this report.')
[void]$sb.AppendLine()
[void]$sb.AppendLine('> The CLI uses a source-generated `JsonSerializerContext`; trimmed self-contained publishing')
[void]$sb.AppendLine('> is expected to succeed. Any failed configuration is omitted above and its final diagnostics')
[void]$sb.AppendLine('> are printed by this script.')

Set-Content -Path $reportPath -Value $sb.ToString() -Encoding utf8
Write-Host ""
Write-Host "Wrote size report to $reportPath" -ForegroundColor Green
Get-Content $reportPath | Write-Host
