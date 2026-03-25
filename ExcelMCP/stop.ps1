param(
  [string]$PidFile = ""
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($PidFile)) {
  $PidFile = Join-Path $env:LOCALAPPDATA 'Revit_MCP\Run\ExcelMCP.pid'
}

if (-not (Test-Path $PidFile)) {
  Write-Host "ExcelMCP pid file not found." -ForegroundColor Yellow
  return
}

try {
  $json = Get-Content $PidFile -Raw | ConvertFrom-Json
  $pid = [int]$json.pid
  Stop-Process -Id $pid -Force -ErrorAction Stop
  Start-Sleep -Milliseconds 300
  if (Test-Path $PidFile) { Remove-Item $PidFile -Force -ErrorAction SilentlyContinue }
  Write-Host "ExcelMCP stopped. PID=$pid" -ForegroundColor Green
} catch {
  Write-Error $_
}
