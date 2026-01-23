[CmdletBinding()]
param(
  [string]$BaseUrl,
  [int]$Port,
  [string]$ExePath = '$env:USERPROFILE\Documents\VS2022\Ver541\ExcelMCP\bin\x64\Release\net8.0-windows\ExcelMCP.exe',
  [switch]$OnlyCheck,
  [switch]$NoStart
)

<#
.SYNOPSIS
  Quick connectivity prep for ExcelMCP.

.DESCRIPTION
  Resolves the target ExcelMCP URL, checks the /health endpoint, and
  (optionally) starts the ExcelMCP.exe binary if it is not running.

  This is intended as a convenience helper before running scripts that call
  ExcelMCP such as:
    - Codex/Manuals/Scripts/apply_from_excelmcp.ps1
    - Codex/Manuals/Scripts/place_rooms_from_excel_labels.ps1

.PARAMETER BaseUrl
  Target ExcelMCP base URL, e.g. 'http://localhost:5215'.
  If omitted, the URL is built from -Port (default 5215).

.PARAMETER Port
  TCP port for ExcelMCP when BaseUrl is not specified. Default: 5215.

.PARAMETER ExePath
  Full path to ExcelMCP.exe. Defaults to the compiled binary path you provided.

.PARAMETER OnlyCheck
  If set, only performs a health check against ExcelMCP and exits.

.PARAMETER NoStart
  If set, does not attempt to start ExcelMCP.exe when the server is not running.

.EXAMPLE
  pwsh -ExecutionPolicy Bypass -File ./connect_excelmcp.ps1

.EXAMPLE
  pwsh -File ./connect_excelmcp.ps1 -BaseUrl 'http://localhost:5216'

.EXAMPLE
  pwsh -File ./connect_excelmcp.ps1 -Port 5215 -ExePath 'D:\Tools\ExcelMCP\ExcelMCP.exe'
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try { chcp 65001 > $null } catch {}

function Resolve-ExcelUrl {
  param(
    [string]$BaseUrl,
    [int]$Port
  )
  if (-not [string]::IsNullOrWhiteSpace($BaseUrl)) {
    return ($BaseUrl.TrimEnd('/'))
  }
  if (-not $Port -or $Port -le 0) {
    $Port = 5215
  }
  return "http://localhost:$Port"
}

function Test-ExcelMcpHealth {
  param(
    [string]$Url,
    [int]$TimeoutSec = 2
  )
  try {
    $res = Invoke-RestMethod -Method GET -Uri ("{0}/health" -f $Url.TrimEnd('/')) -TimeoutSec $TimeoutSec -ErrorAction Stop
    if ($res -and $res.ok -eq $true) { return $true }
  } catch {
    return $false
  }
  return $false
}

$excelUrl = Resolve-ExcelUrl -BaseUrl $BaseUrl -Port $Port
Write-Host "[ExcelMCP] Target URL: $excelUrl" -ForegroundColor Cyan

Write-Host "[ExcelMCP] Checking health endpoint ..." -ForegroundColor Cyan
$healthy = Test-ExcelMcpHealth -Url $excelUrl -TimeoutSec 2

if ($healthy) {
  Write-Host "[ExcelMCP] Server is already running." -ForegroundColor Green
  if ($OnlyCheck) { exit 0 }
} else {
  Write-Host "[ExcelMCP] No healthy server detected at $excelUrl" -ForegroundColor Yellow
  if ($OnlyCheck -or $NoStart) {
    Write-Host "[ExcelMCP] Skipping start because OnlyCheck/NoStart is set." -ForegroundColor Yellow
    exit 1
  }

  if (-not (Test-Path -Path $ExePath -PathType Leaf)) {
    throw "ExcelMCP.exe not found at ExePath: $ExePath"
  }

  $exeDir = Split-Path -Path $ExePath -Parent
  Write-Host "[ExcelMCP] Starting ExcelMCP.exe ..." -ForegroundColor Cyan
  Write-Host "  Path : $ExePath" -ForegroundColor Gray
  Write-Host "  Url  : $excelUrl" -ForegroundColor Gray

  # Set ASPNETCORE_URLS so the server binds to the requested URL.
  $env:ASPNETCORE_URLS = $excelUrl

  $proc = Start-Process -FilePath $ExePath -WorkingDirectory $exeDir -WindowStyle Minimized -PassThru -ErrorAction Stop

  # Wait for the server to become healthy
  $maxWaitSec = 30
  $waited = 0
  do {
    Start-Sleep -Seconds 1
    $waited++
    $healthy = Test-ExcelMcpHealth -Url $excelUrl -TimeoutSec 2
  } while (-not $healthy -and $waited -lt $maxWaitSec)

  if (-not $healthy) {
    Write-Host "[ExcelMCP] Failed to confirm health within $maxWaitSec seconds." -ForegroundColor Red
    Write-Host "  - Check ExcelMCP logs or run it manually from: $exeDir" -ForegroundColor Yellow
    exit 1
  }

  Write-Host "[ExcelMCP] Server started successfully (PID=$($proc.Id))." -ForegroundColor Green
}

Write-Host "[ExcelMCP] Ready for Excel operations via: $excelUrl" -ForegroundColor Green
Write-Host "Examples:" -ForegroundColor Cyan
Write-Host "  # Simple health check" -ForegroundColor Gray
Write-Host "  Invoke-RestMethod $excelUrl/health | ConvertTo-Json -Compress" -ForegroundColor Gray
Write-Host "  # File-based helpers (no Excel process required)" -ForegroundColor Gray
Write-Host "  Invoke-RestMethod -Method Post -Uri '$excelUrl/read_cells' -ContentType 'application/json' -Body (@{ excelPath='C:\\path\\book.xlsx'; sheetName='Sheet1'; rangeA1='A1:C5'; returnRaw=\$true } | ConvertTo-Json)" -ForegroundColor Gray

Write-Host "Done." -ForegroundColor Cyan

