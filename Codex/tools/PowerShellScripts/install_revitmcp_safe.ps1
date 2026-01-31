# @feature: install revitmcp safe | keywords: misc
<#
.SYNOPSIS
  Safe installer for RevitMCP add-in and server.

.DESCRIPTION
  - Stops Revit / RevitMCPServer if running (to avoid locked/partial copies).
  - Copies add-in binaries (Release x64) to %APPDATA%\Autodesk\Revit\Addins\2024\RevitMCPAddin, excluding server.
  - Copies server from publish output to the add-in server folder.
  - Verifies SHA256 hash of RevitMCPServer.exe (source vs destination).

.PARAMETER AddinSrc
  Source folder for add-in DLLs (default: ..\RevitMCPAddin\bin\x64\Release).

.PARAMETER ServerSrc
  Source folder for server publish output (default: ..\RevitMCPServer\publish).

.PARAMETER Dest
  Destination add-in folder (default: %APPDATA%\Autodesk\Revit\Addins\2024\RevitMCPAddin).

.EXAMPLE
  pwsh -ExecutionPolicy Bypass -File ./install_revitmcp_safe.ps1

.NOTES
  - Requires robocopy (Windows標準)。
  - Revitを終了してから実行してください。
#>

[CmdletBinding()]
param(
  [string]$AddinSrc = "..\RevitMCPAddin\bin\x64\Release",
  [string]$ServerSrc = "..\RevitMCPServer\publish",
  [string]$Dest = "$env:APPDATA\Autodesk\Revit\Addins\2024\RevitMCPAddin"
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

function Copy-Tree {
  param(
    [string]$Source,
    [string]$Target,
    [string[]]$ExcludeDirs = @()
  )
  if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
    throw "Source not found: $Source"
  }
  if (-not (Test-Path -LiteralPath $Target -PathType Container)) {
    New-Item -ItemType Directory -Path $Target | Out-Null
  }
  $xd = $ExcludeDirs | ForEach-Object { "/XD `"$($_)`"" }
  $cmd = @("robocopy", "`"$Source`"", "`"$Target`"", "/MIR", "/R:3", "/W:2")
  if ($ExcludeDirs.Count -gt 0) { $cmd += $xd }
  $robocopyCmd = $cmd -join " "
  Write-Host "[Copy] $robocopyCmd" -ForegroundColor Cyan
  cmd /c $robocopyCmd | Out-Host
  $rc = $LASTEXITCODE
  if ($rc -gt 3) {
    throw "robocopy failed (exit $rc)"
  }
}

function Get-Hash($path) {
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "File not found: $path" }
  return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}

# Resolve paths
$addinSrcFull = Resolve-Path $AddinSrc
$serverSrcFull = Resolve-Path $ServerSrc
$destFull = Resolve-Path -LiteralPath $Dest -ErrorAction SilentlyContinue
if (-not $destFull) { $destFull = (New-Item -ItemType Directory -Path $Dest).FullName }
$destServer = Join-Path $destFull "server"

Write-Host "AddinSrc : $addinSrcFull"
Write-Host "ServerSrc: $serverSrcFull"
Write-Host "Dest     : $destFull"
Write-Host "DestSrv  : $destServer"

# 1) Stop running Revit / server
Stop-IfRunning -Names @("RevitMCPServer", "Revit")

# 2) Copy add-in (exclude server folder)
Copy-Tree -Source $addinSrcFull -Target $destFull -ExcludeDirs @("server")

# 3) Copy server (full publish)
Copy-Tree -Source $serverSrcFull -Target $destServer

# 4) Hash check for exe
$srcExe = Join-Path $serverSrcFull "RevitMCPServer.exe"
$dstExe = Join-Path $destServer "RevitMCPServer.exe"
$h1 = Get-Hash $srcExe
$h2 = Get-Hash $dstExe
if ($h1 -ne $h2) { throw "Hash mismatch for RevitMCPServer.exe (src:$h1 dst:$h2)" }

Write-Host "[OK] Install completed. Hash verified." -ForegroundColor Green
