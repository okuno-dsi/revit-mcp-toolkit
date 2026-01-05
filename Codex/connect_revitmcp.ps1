<#
.SYNOPSIS
  Quick connectivity prep to Revit MCP.

.DESCRIPTION
  Checks the MCP port, runs the bootstrap script to collect environment info,
  and prints the path to the saved JSON plus the activeViewId.

.PARAMETER Port
  Target Revit MCP port. If omitted, uses $env:REVIT_MCP_PORT or 5210.

.PARAMETER OnlyCheck
  If set, only performs the TCP port check and exits.

.EXAMPLE
  pwsh -ExecutionPolicy Bypass -File ./connect_revitmcp.ps1 -Port 5210

.EXAMPLE
  pwsh -File ./connect_revitmcp.ps1
  (uses $env:REVIT_MCP_PORT or 5210)

#>

[CmdletBinding()]
param(
  [int]$Port,
  [switch]$OnlyCheck
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Ensure UTF-8 code page for consistent console output on Windows
try { chcp 65001 > $null } catch {}

function Resolve-Port {
  param([int]$Port)
  if ($PSBoundParameters.ContainsKey('Port') -and $Port) { return $Port }
  if ($env:REVIT_MCP_PORT -as [int]) { return [int]$env:REVIT_MCP_PORT }
  return 5210
}

$usePort = Resolve-Port -Port $Port

Write-Host "[RevitMCP] Checking TCP port $usePort ..." -ForegroundColor Cyan
$portOk = $false
try {
  $tnc = Test-NetConnection -ComputerName 'localhost' -Port $usePort -WarningAction SilentlyContinue
  $portOk = [bool]$tnc.TcpTestSucceeded
} catch {
  Write-Warning "Test-NetConnection failed: $($_.Exception.Message)"
}

if (-not $portOk) {
  Write-Warning "Port $usePort is not reachable on localhost."
  Write-Host "Tips:" -ForegroundColor Yellow
  Write-Host " - Ensure Revit is running and the MCP add-in is active (port in Revit UI/logs)."
  Write-Host " - Confirm firewall is not blocking 127.0.0.1:$usePort."
  Write-Host " - Try a different port or set `$env:REVIT_MCP_PORT."
  if ($OnlyCheck) { exit 1 }
} else {
  Write-Host "Port check OK (TcpTestSucceeded=True)." -ForegroundColor Green
  if ($OnlyCheck) { exit 0 }
}

# Run the existing bootstrap script to collect environment info
$scriptPath = Join-Path -Path $PSScriptRoot -ChildPath 'Manuals/Scripts/test_connection.ps1'
if (-not (Test-Path -Path $scriptPath -PathType Leaf)) {
  throw "Bootstrap script not found: $scriptPath"
}

Write-Host "[RevitMCP] Running bootstrap via test_connection.ps1 ..." -ForegroundColor Cyan

& pwsh -ExecutionPolicy Bypass -File $scriptPath -Port $usePort | Out-Host

# Attempt to locate the output file and extract activeViewId
$workDir = Join-Path -Path $PSScriptRoot -ChildPath 'Work'
$logs = Get-ChildItem -Path $workDir -Filter 'agent_bootstrap.json' -Recurse -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
if (-not $logs -or $logs.Count -eq 0) {
  # Fallback to Manuals/Logs if Work path not created by script
  $fallback = Join-Path -Path $PSScriptRoot -ChildPath 'Manuals/Logs/agent_bootstrap.json'
  if (Test-Path $fallback) {
    $logs = ,(Get-Item $fallback)
  }
}

if ($logs -and $logs.Count -gt 0) {
  $latest = $logs[0].FullName
  Write-Host "[RevitMCP] Bootstrap saved: `n  $latest" -ForegroundColor Green
  try {
    $json = Get-Content -Raw -Encoding UTF8 -Path $latest | ConvertFrom-Json
    $activeViewId = $json.result.result.environment.activeViewId
    $activeViewIdLong = 0L
    if ([long]::TryParse("$activeViewId", [ref]$activeViewIdLong) -and $activeViewIdLong -gt 0) {
      Write-Host "Active View ID: $activeViewIdLong" -ForegroundColor Green
    } else {
      Write-Host "Active View ID not found or invalid in the bootstrap JSON." -ForegroundColor Yellow
    }
  } catch {
    Write-Host "Note: could not parse activeViewId from bootstrap JSON: $($_.Exception.Message)" -ForegroundColor Yellow
  }
} else {
  Write-Host "Bootstrap output not found. Check script output above for hints." -ForegroundColor Yellow
}

Write-Host "[Next] List elements in active view:" -ForegroundColor Cyan
Write-Host "  pwsh -ExecutionPolicy Bypass -File ./Manuals/Scripts/list_elements_in_view.ps1 -Port $usePort" -ForegroundColor Gray

Write-Host "Done." -ForegroundColor Cyan
