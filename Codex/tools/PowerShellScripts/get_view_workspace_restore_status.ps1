# @feature: get view workspace restore status | keywords: スペース, ビュー
param(
  [int]$Port = 5210
)

$useEnv = $false
if(-not $PSBoundParameters.ContainsKey('Port') -and $env:REVIT_MCP_PORT){
  try { $Port = [int]$env:REVIT_MCP_PORT; $useEnv = $true } catch {}
}

$SCRIPT_DIR = $PSScriptRoot
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'

function Resolve-LogsDir([int]$p){
  $work = Resolve-Path (Join-Path $SCRIPT_DIR '..\\..\\..\\Projects')
  $cands = Get-ChildItem -LiteralPath $work -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*_$p" }
  $chosen = $null
  if($cands){ $chosen = ($cands | Where-Object { $_.Name -notlike 'Project_*' } | Select-Object -First 1); if(-not $chosen){ $chosen = $cands | Select-Object -First 1 } }
  if(-not $chosen){ $chosen = New-Item -ItemType Directory -Path (Join-Path $work ("Project_{0}" -f $p)) }
  $logs = Join-Path $chosen.FullName 'Logs'
  if(-not (Test-Path $logs)){ New-Item -ItemType Directory -Path $logs | Out-Null }
  return $logs
}
$LOGS = Resolve-LogsDir -p $Port

chcp 65001 > $null
$env:PYTHONUTF8='1'
if($useEnv){ Write-Host "[Port] Using REVIT_MCP_PORT=$Port" -ForegroundColor DarkCyan }

$outPath = Join-Path $LOGS 'get_view_workspace_restore_status.json'
Write-Host "[get_view_workspace_restore_status] -> $outPath" -ForegroundColor Cyan
python $PY --port $Port --command get_view_workspace_restore_status --output-file $outPath



