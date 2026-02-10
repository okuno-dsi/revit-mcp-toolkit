# @feature: install codexgui safe | keywords: misc
<#
.SYNOPSIS
  Safe installer for CodexGUI without overwriting user logs/settings.

.DESCRIPTION
  - Copies CodexGUI build output to the user install folder.
  - Does NOT overwrite or delete user settings/logs.
  - Keeps any existing user files in the destination.

.PARAMETER Source
  Source folder for CodexGUI build output
  (default: ..\CodexGui\bin\Release\net6.0-windows).

.PARAMETER Dest
  Destination folder (default: %USERPROFILE%\Documents\Codex_MCP\CodexGui).

.EXAMPLE
  pwsh -ExecutionPolicy Bypass -File ./install_codexgui_safe.ps1

.NOTES
  - Uses robocopy (Windows標準).
  - Intentionally avoids /MIR to preserve user files.
#>

[CmdletBinding()]
param(
  [string]$Source = (Join-Path $PSScriptRoot "..\..\CodexGui\bin\Release\net6.0-windows"),
  [string]$Dest = ""
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

function Copy-TreeSafe {
  param(
    [string]$Source,
    [string]$Target
  )
  if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
    throw "Source not found: $Source"
  }
  if (-not (Test-Path -LiteralPath $Target -PathType Container)) {
    New-Item -ItemType Directory -Path $Target | Out-Null
  }

  $excludeFiles = @(
    "CodexGuiSessions.json",
    "CodexGuiUiSettings.json",
    "codexgui.log",
    "codexgui_*.log",
    "*.log"
  )
  $excludeDirs = @(
    "GUI_Log",
    "Logs"
  )

  $xf = $excludeFiles | ForEach-Object { "/XF `"$($_)`"" }
  $xd = $excludeDirs | ForEach-Object { "/XD `"$($_)`"" }

  $cmd = @(
    "robocopy",
    "`"$Source`"",
    "`"$Target`"",
    "/E",
    "/R:3",
    "/W:2"
  )
  if ($excludeFiles.Count -gt 0) { $cmd += $xf }
  if ($excludeDirs.Count -gt 0) { $cmd += $xd }

  $robocopyCmd = $cmd -join " "
  Write-Host "[Copy] $robocopyCmd" -ForegroundColor Cyan
  cmd /c $robocopyCmd | Out-Host
  $rc = $LASTEXITCODE
  if ($rc -gt 3) {
    throw "robocopy failed (exit $rc)"
  }
}

function Resolve-DefaultDest {
  # 1) paths.json (LocalAppData\RevitMCP)
  try {
    $paths = Join-Path $env:LOCALAPPDATA 'RevitMCP\paths.json'
    if (Test-Path -LiteralPath $paths -PathType Leaf) {
      $cfg = Get-Content -LiteralPath $paths -Raw -Encoding UTF8 | ConvertFrom-Json
      if ($cfg.root) {
        $p = Join-Path $cfg.root 'CodexGui'
        if (Test-Path -LiteralPath $p -PathType Container) { return $p }
      }
      if ($cfg.appsRoot) {
        $p = Join-Path $cfg.appsRoot 'CodexGui'
        if (Test-Path -LiteralPath $p -PathType Container) { return $p }
      }
    }
  } catch {}

  # 2) Documents\Revit_MCP\Apps\CodexGui (preferred)
  $docs = [Environment]::GetFolderPath('MyDocuments')
  if ($docs) {
    $p = Join-Path $docs 'Revit_MCP\Apps\CodexGui'
    if (Test-Path -LiteralPath $p -PathType Container) { return $p }
  }

  # 3) Documents\Codex_MCP\CodexGui (legacy)
  if ($docs) {
    $p = Join-Path $docs 'Codex_MCP\CodexGui'
    return $p
  }

  # 4) Fallback
  return (Join-Path $env:USERPROFILE 'Documents\Revit_MCP\Apps\CodexGui')
}

$srcFull = Resolve-Path $Source
if (-not $Dest) { $Dest = Resolve-DefaultDest }
$destFull = Resolve-Path -LiteralPath $Dest -ErrorAction SilentlyContinue
if (-not $destFull) { $destFull = (New-Item -ItemType Directory -Path $Dest).FullName }

Write-Host "Source: $srcFull"
Write-Host "Dest  : $destFull"

Stop-IfRunning -Names @("CodexGui")

Copy-TreeSafe -Source $srcFull -Target $destFull

Write-Host "[OK] CodexGUI installed without overwriting user settings/logs." -ForegroundColor Green

