param(
  [string]$PidFile = "",
  [string]$LogDir = ""
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($PidFile)) {
  $PidFile = Join-Path $env:LOCALAPPDATA 'Revit_MCP\Run\ExcelMCP.pid'
}
if ([string]::IsNullOrWhiteSpace($LogDir)) {
  $LogDir = Join-Path $env:LOCALAPPDATA 'Revit_MCP\Logs\ExcelMCP'
}

$running = $false
$pid = $null

if (Test-Path $PidFile) {
  try {
    $json = Get-Content $PidFile -Raw | ConvertFrom-Json
    $pid = [int]$json.pid
    $null = Get-Process -Id $pid -ErrorAction Stop
    $running = $true
  } catch {
    $running = $false
  }
}

[pscustomobject]@{
  running = $running
  pid = $pid
  pidFile = $PidFile
  logDir = $LogDir
}
