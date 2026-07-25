#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Measures the published binary size of the msixmgr CLI across deployment configurations.

.DESCRIPTION
    Publishes src/msixmgr in three configurations:
      1. Framework-dependent (portable, requires the .NET 10 runtime on the target).
      2. Self-contained win-x64 (bundles the .NET runtime).
      3. Self-contained win-x64 + trimmed (IL-trimmed; only built if it succeeds).

    For each configuration it records the total published output size and the size of the
    key assemblies, then writes a Markdown summary to bench/size-report.md.

    A comparison against the original C++ MSIX Core (msixmgr.exe / MsixCore.dll) is a FUTURE
    step: those binaries are not part of this repository. The intended methodology is documented
    in the generated report.

.EXAMPLE
    pwsh bench/Measure-Size.ps1
#>
[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src/msixmgr/msixmgr.csproj'
$publishRoot = Join-Path $PSScriptRoot 'publish'
$reportPath = Join-Path $PSScriptRoot 'size-report.md'

if (-not (Test-Path $project)) {
    throw "Cannot find msixmgr project at $project"
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

    $exe = $files | Where-Object { $_.Name -in @('msixmgr.exe', 'msixmgr') } | Select-Object -First 1
    $keyAssemblies = $files |
        Where-Object { $_.Extension -in @('.dll', '.exe') -and $_.Name -like 'MsixCore*' -or $_.Name -like 'msixmgr*' } |
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

$results += Publish-Config -Name 'Framework-dependent (portable)' `
    -OutDir (Join-Path $publishRoot 'framework-dependent') `
    -ExtraArgs @('-r', $Runtime, '--self-contained', 'false')

$results += Publish-Config -Name "Self-contained ($Runtime)" `
    -OutDir (Join-Path $publishRoot 'self-contained') `
    -ExtraArgs @('-r', $Runtime, '--self-contained', 'true')

$results += Publish-Config -Name "Self-contained + trimmed ($Runtime)" `
    -OutDir (Join-Path $publishRoot 'self-contained-trimmed') `
    -ExtraArgs @('-r', $Runtime, '--self-contained', 'true', '-p:PublishTrimmed=true')

$results = $results | Where-Object { $null -ne $_ }

# --- Emit report -----------------------------------------------------------
$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine('# msixmgr published-size report')
[void]$sb.AppendLine()
[void]$sb.AppendLine("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')")
[void]$sb.AppendLine()
[void]$sb.AppendLine("- Runtime identifier: ``$Runtime``")
[void]$sb.AppendLine("- Configuration: ``$Configuration``")
[void]$sb.AppendLine("- .NET SDK: ``$(dotnet --version)``")
[void]$sb.AppendLine("- Host OS: ``$([System.Runtime.InteropServices.RuntimeInformation]::OSDescription.Trim())``")
[void]$sb.AppendLine()

[void]$sb.AppendLine('## Totals')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| Configuration | Total size | Files | msixmgr host |')
[void]$sb.AppendLine('| --- | ---: | ---: | ---: |')
foreach ($r in $results) {
    [void]$sb.AppendLine("| $($r.Name) | $(Format-Size $r.TotalBytes) | $($r.FileCount) | $(Format-Size $r.ExeBytes) |")
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
[void]$sb.AppendLine('The original C++ `msixmgr.exe` and `MsixCore` binaries are **not** part of this')
[void]$sb.AppendLine('repository, so a direct size comparison cannot be produced here yet. Intended methodology:')
[void]$sb.AppendLine()
[void]$sb.AppendLine('1. Obtain an official release build of the C++ MSIX Core `msixmgr.exe` (and its')
[void]$sb.AppendLine('   dependent DLLs) for `win-x64` from the upstream `microsoft/msix-packaging` project.')
[void]$sb.AppendLine('2. Record the on-disk size of the shipped executable + DLLs (the C++ build has no')
[void]$sb.AppendLine('   managed runtime, so its natural analogue is the **framework-dependent** column,')
[void]$sb.AppendLine('   while the **self-contained** column reflects the true "no prerequisites" install size).')
[void]$sb.AppendLine('3. Compare like-for-like: framework-dependent .NET vs. C++ needing the OS CRT/redist;')
[void]$sb.AppendLine('   self-contained/trimmed .NET vs. C++ statically linked, if available.')
[void]$sb.AppendLine('4. Track both totals and the "core packaging" binary size (`MsixCore.Packaging.dll`')
[void]$sb.AppendLine('   vs. the C++ `msix.dll`) over time in this report.')
[void]$sb.AppendLine()
[void]$sb.AppendLine('> Note: trimmed self-contained size depends on trimming succeeding for the CLI. On this')
[void]$sb.AppendLine('> repository the trimmed configuration currently FAILS to publish: the `inspect`, `validate`,')
[void]$sb.AppendLine('> and `unpack` verbs use reflection-based `System.Text.Json.JsonSerializer.Serialize`, which')
[void]$sb.AppendLine('> raises trim-analysis errors IL2026 (warnings-as-errors). Making the CLI trim-safe (source-')
[void]$sb.AppendLine('> generated `JsonSerializerContext`) would unlock a materially smaller self-contained size.')

Set-Content -Path $reportPath -Value $sb.ToString() -Encoding utf8
Write-Host ""
Write-Host "Wrote size report to $reportPath" -ForegroundColor Green
Get-Content $reportPath | Write-Host
