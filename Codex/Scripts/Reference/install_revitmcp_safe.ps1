<#
.SYNOPSIS
  Safe installer for RevitMCP add-in and server.

.DESCRIPTION
  - Stops Revit / RevitMCPServer if running (to avoid locked/partial copies).
  - Copies add-in binaries to %APPDATA%\Autodesk\Revit\Addins\<year>\RevitMCPAddin.
  - Copies server from publish output to the add-in server folder.
  - Never mirrors the whole add-in root from a source that does not contain server.
  - Writes year-local RevitMCPAddin.addin manifests.
  - Verifies SHA256 hash of RevitMCPServer.exe (source vs destination).
  - Uses non-destructive copy (/E), so destination extras are not deleted.

.PARAMETER RevitYears
  Target Revit major years. Default: 2025, 2026.

.PARAMETER AddinSrcRoot
  Root folder containing year-specific add-in outputs.
  Default: ..\..\..\Artifacts\RevitMCPAddin

.PARAMETER ServerSrc
  Source folder for server publish output.
  Default: ..\..\..\Artifacts\RevitMCPServer\Server\bin\x64\Release\net8.0-windows

.PARAMETER AddinsRoot
  Destination root for Revit add-ins.
  Default: %APPDATA%\Autodesk\Revit\Addins

.EXAMPLE
  pwsh -ExecutionPolicy Bypass -File .\install_revitmcp_safe.ps1

.EXAMPLE
  pwsh -ExecutionPolicy Bypass -File .\install_revitmcp_safe.ps1 -RevitYears 2025

.NOTES
  - Requires robocopy (Windows standard).
  - Revit should be closed before execution.
#>

[CmdletBinding()]
param(
  [int[]]$RevitYears = @(2025, 2026),
  [string]$AddinSrcRoot = "",
  [string]$ServerSrc = "",
  [string]$AddinsRoot = "",
  [string]$AddinName = "RevitMCPAddin"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Stop-IfRunning {
  param([string[]]$Names)
  foreach ($n in $Names) {
    Get-Process -Name $n -ErrorAction SilentlyContinue | ForEach-Object {
      Write-Host "[Stop] $($_.ProcessName) (Id=$($_.Id))" -ForegroundColor Yellow
      try { $_ | Stop-Process -Force -ErrorAction Stop } catch { Write-Warning "Could not stop $($_.ProcessName): $($_.Exception.Message)" }
    }
  }
}

function Ensure-Dir {
  param([string]$Path)
  if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
  }
  return (Resolve-Path -LiteralPath $Path).Path
}

function Copy-Tree {
  param(
    [string]$Source,
    [string]$Target,
    [string[]]$ExcludeDirs = @()
  )
  if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
    throw "Source not found: $Source"
  }
  Ensure-Dir $Target | Out-Null
  $xd = @()
  foreach ($d in $ExcludeDirs) {
    $xd += "/XD `"$([System.IO.Path]::Combine($Source, $d))`""
  }
  $cmd = @("robocopy", "`"$Source`"", "`"$Target`"", "/E", "/R:3", "/W:2", "/NFL", "/NDL")
  if ($ExcludeDirs.Count -gt 0) { $cmd += $xd }
  $robocopyCmd = $cmd -join " "
  Write-Host "[Copy] $robocopyCmd" -ForegroundColor Cyan
  cmd /c $robocopyCmd | Out-Host
  $rc = $LASTEXITCODE
  if ($rc -gt 3) {
    throw "robocopy failed (exit $rc)"
  }
}

function Get-Hash {
  param([string]$Path)
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "File not found: $Path" }
  return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Assert-ServerSourcePayload {
  param([string]$ServerSourceDir)

  if (-not (Test-Path -LiteralPath $ServerSourceDir -PathType Container)) {
    throw "Server source not found: $ServerSourceDir"
  }

  $required = @(
    'RevitMCPServer.exe',
    'RevitMCPServer.dll',
    'RevitMCPServer.runtimeconfig.json',
    'RevitMCPServer.deps.json',
    'appsettings.json'
  )
  foreach ($rel in $required) {
    $full = Join-Path $ServerSourceDir $rel
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
      throw "Server source is incomplete: missing $full"
    }
  }

  $serverFileCount = (Get-ChildItem -LiteralPath $ServerSourceDir -Recurse -File -ErrorAction Stop | Measure-Object).Count
  if ($serverFileCount -lt 80) {
    throw "Server source looks incomplete: only $serverFileCount files under $ServerSourceDir"
  }
}

function Resolve-ExistingDirectory {
  param([string[]]$Candidates, [string]$Label)
  foreach ($candidate in $Candidates) {
    if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
    if (Test-Path -LiteralPath $candidate -PathType Container) {
      return (Resolve-Path -LiteralPath $candidate).Path
    }
  }
  throw "$Label not found. Candidates:`n - " + ($Candidates -join "`n - ")
}

function Resolve-AddinSourceForYear {
  param([int]$Year, [string]$RepoRoot, [string]$SrcRoot)

  $candidates = @()
  if ($Year -ge 2025) {
    $candidates += (Join-Path $SrcRoot "$Year\bin\Release\net8.0-windows")
    $candidates += (Join-Path $SrcRoot "$Year")
  }
  if ($Year -eq 2024) {
    $candidates += (Join-Path $RepoRoot "RevitMCPAddin\bin\x64\Release")
  }
  return Resolve-ExistingDirectory -Candidates $candidates -Label "Add-in source for Revit $Year"
}

function Test-RevitInstalled {
  param([int]$Year)

  $roots = @()
  if ($env:ProgramW6432) { $roots += $env:ProgramW6432 }
  if ($env:ProgramFiles) { $roots += $env:ProgramFiles }
  $roots = $roots | Select-Object -Unique
  foreach ($root in $roots) {
    $candidate = Join-Path $root ("Autodesk\\Revit {0}" -f $Year)
    if (Test-Path -LiteralPath $candidate -PathType Container) { return $true }
  }
  return $false
}

function Remove-InstallerLegacyPath {
  param(
    [string]$Path,
    [string]$ServerRoot,
    [switch]$Directory
  )

  if ([string]::IsNullOrWhiteSpace($Path) -or [string]::IsNullOrWhiteSpace($ServerRoot)) { return }
  if (-not (Test-Path -LiteralPath $Path)) { return }

  $resolvedPath = [System.IO.Path]::GetFullPath($Path)
  $resolvedServerRoot = [System.IO.Path]::GetFullPath($ServerRoot)
  if (-not $resolvedPath.StartsWith($resolvedServerRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "削除対象が server 配下ではありません: $resolvedPath"
  }

  if ($Directory) {
    $leaf = Split-Path -Leaf $resolvedPath
    if ($leaf -notin @('CaptureAgent', 'win-x64', 'tessdata')) {
      throw "許可されていないディレクトリ削除対象です: $resolvedPath"
    }
    Remove-Item -LiteralPath $resolvedPath -Recurse -Force -ErrorAction Stop
  } else {
    $name = Split-Path -Leaf $resolvedPath
    if ($name -notlike 'RevitMcp.CaptureAgent.*') {
      throw "許可されていないファイル削除対象です: $resolvedPath"
    }
    Remove-Item -LiteralPath $resolvedPath -Force -ErrorAction Stop
  }
}

function Remove-LegacyCaptureAgentDupes {
  param([string]$AddinsYearRoot, [string]$AddinName)

  $serverRoot = Join-Path $AddinsYearRoot (Join-Path $AddinName 'server')
  if (-not (Test-Path -LiteralPath $serverRoot -PathType Container)) { return }

  $legacyDir = Join-Path $serverRoot 'CaptureAgent'
  if (Test-Path -LiteralPath $legacyDir -PathType Container) {
    Remove-InstallerLegacyPath -Path $legacyDir -ServerRoot $serverRoot -Directory
  }

  $dupPublish = Join-Path $serverRoot 'capture-agent\\win-x64'
  if (Test-Path -LiteralPath $dupPublish -PathType Container) {
    Remove-InstallerLegacyPath -Path $dupPublish -ServerRoot $serverRoot -Directory
  }

  $legacyFiles = Get-ChildItem -LiteralPath $serverRoot -Filter 'RevitMcp.CaptureAgent.*' -File -ErrorAction SilentlyContinue
  foreach ($f in $legacyFiles) {
    Remove-InstallerLegacyPath -Path $f.FullName -ServerRoot $serverRoot
  }

  $legacyTess = Join-Path $serverRoot 'tessdata'
  if (Test-Path -LiteralPath $legacyTess -PathType Container) {
    Remove-InstallerLegacyPath -Path $legacyTess -ServerRoot $serverRoot -Directory
  }
}

function Write-AddinManifest {
  param(
    [string]$YearRoot,
    [string]$AddinFolderName
  )

  $manifestPath = Join-Path $YearRoot "$AddinFolderName.addin"
  $manifest = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>$AddinFolderName</Name>
    <Assembly>$AddinFolderName\$AddinFolderName.dll</Assembly>
    <AddInId>1B22E885-40C8-4E18-BB5D-FCC2D7D8C272</AddInId>
    <FullClassName>RevitMCPAddin.App</FullClassName>
    <VendorId>DAIKEN</VendorId>
    <VendorDescription>Revit MCP Add-in</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
  Set-Content -LiteralPath $manifestPath -Value $manifest -Encoding UTF8
  return $manifestPath
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
if ([string]::IsNullOrWhiteSpace($AddinSrcRoot)) {
  $AddinSrcRoot = Join-Path $repoRoot "Artifacts\RevitMCPAddin"
}
if ([string]::IsNullOrWhiteSpace($AddinsRoot)) {
  $AddinsRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins"
}
if ([string]::IsNullOrWhiteSpace($ServerSrc)) {
  $ServerSrc = Resolve-ExistingDirectory -Candidates @(
    (Join-Path $repoRoot "Artifacts\RevitMCPServer\Server\bin\x64\Release\net8.0-windows"),
    (Join-Path $repoRoot "RevitMCPAddin\bin\x64\Release\server"),
    (Join-Path $repoRoot "RevitMCPServer\publish")
  ) -Label "Server source"
}

$addinsRootFull = Ensure-Dir $AddinsRoot
$serverSrcFull = (Resolve-Path -LiteralPath $ServerSrc).Path
Assert-ServerSourcePayload -ServerSourceDir $serverSrcFull
$srcExe = Join-Path $serverSrcFull "RevitMCPServer.exe"

Write-Host "RepoRoot  : $repoRoot"
Write-Host "AddinsRoot: $addinsRootFull"
Write-Host "ServerSrc : $serverSrcFull"
Write-Host "Years     : $($RevitYears -join ', ')"

Stop-IfRunning -Names @("RevitMCPServer", "Revit")

$targetYears = @($RevitYears | Where-Object { Test-RevitInstalled -Year $_ })
$skippedYears = @($RevitYears | Where-Object { $_ -notin $targetYears })
if ($skippedYears.Count -gt 0) {
  Write-Host ("[Skip] Revit 本体が見つからない年: {0}" -f ($skippedYears -join ', ')) -ForegroundColor Yellow
}

foreach ($year in $targetYears) {
  $addinSrcFull = Resolve-AddinSourceForYear -Year $year -RepoRoot $repoRoot -SrcRoot $AddinSrcRoot
  $yearRoot = Ensure-Dir (Join-Path $addinsRootFull ([string]$year))
  $destFull = Ensure-Dir (Join-Path $yearRoot $AddinName)
  $destServer = Ensure-Dir (Join-Path $destFull "server")

  Write-Host ""
  Write-Host "=== Revit $year ===" -ForegroundColor Green
  Write-Host "AddinSrc : $addinSrcFull"
  Write-Host "Dest     : $destFull"
  Write-Host "DestSrv  : $destServer"

  Copy-Tree -Source $addinSrcFull -Target $destFull -ExcludeDirs @("server")
  Copy-Tree -Source $serverSrcFull -Target $destServer
  Remove-LegacyCaptureAgentDupes -AddinsYearRoot $yearRoot -AddinName $AddinName
  $manifestPath = Write-AddinManifest -YearRoot $yearRoot -AddinFolderName $AddinName

  $dstExe = Join-Path $destServer "RevitMCPServer.exe"
  $h1 = Get-Hash $srcExe
  $h2 = Get-Hash $dstExe
  if ($h1 -ne $h2) {
    throw "Hash mismatch for RevitMCPServer.exe (year=$year src:$h1 dst:$h2)"
  }

  Write-Host "[OK] Revit $year installed. Manifest: $manifestPath" -ForegroundColor Green
}

Write-Host ""
if ($targetYears.Count -gt 0) {
  Write-Host "[OK] Install completed for years: $($targetYears -join ', ')" -ForegroundColor Green
} else {
  Write-Host "[OK] Revit 本体が見つからなかったため、Add-in のインストールはスキップしました。" -ForegroundColor Yellow
}
