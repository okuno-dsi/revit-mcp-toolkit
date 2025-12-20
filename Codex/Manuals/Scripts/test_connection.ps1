param(
  [int]$Port = 5210
)
chcp 65001 > $null
$env:PYTHONUTF8='1'
$useEnv = $false
if(-not $PSBoundParameters.ContainsKey('Port') -and $env:REVIT_MCP_PORT){
  try { $Port = [int]$env:REVIT_MCP_PORT; $useEnv = $true } catch {}
}
$SCRIPT_DIR = $PSScriptRoot
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'

# Resolve project logs directory under Work/<Project>_<Port>/Logs, fallback to Manuals/Logs
function Get-LogsDir([int]$p){
  $work = Resolve-Path (Join-Path $SCRIPT_DIR '..\..\Work')
  $cands = Get-ChildItem -LiteralPath $work -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*_$p" }
  $chosen = $null
  if($cands){
    $chosen = ($cands | Where-Object { $_.Name -notlike 'Project_*' } | Select-Object -First 1)
    if(-not $chosen){ $chosen = $cands | Select-Object -First 1 }
  }
  if(-not $chosen){ $chosen = New-Item -ItemType Directory -Path (Join-Path $work ("Project_{0}" -f $p)) }
  $logs = Join-Path $chosen.FullName 'Logs'
  if(-not (Test-Path $logs)){ New-Item -ItemType Directory -Path $logs | Out-Null }
  return $logs
}
$LOGS = Get-LogsDir -p $Port
if($useEnv){ Write-Host "[Port] Using REVIT_MCP_PORT=$Port" -ForegroundColor DarkCyan }
Write-Host "[Ping]" -ForegroundColor Cyan
python $PY --port $Port --command ping_server
Write-Host "[Bootstrap] -> $LOGS" -ForegroundColor Cyan
python $PY --port $Port --command agent_bootstrap --output-file (Join-Path $LOGS 'agent_bootstrap.json')

