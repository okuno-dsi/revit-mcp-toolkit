# @feature: list elements in view | keywords: ビュー
param(
  [int]$Port = 5210,
  [int]$ViewId
)
$useEnv = $false
if(-not $PSBoundParameters.ContainsKey('Port') -and $env:REVIT_MCP_PORT){
  try { $Port = [int]$env:REVIT_MCP_PORT; $useEnv = $true } catch {}
}
$SCRIPT_DIR = $PSScriptRoot
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'

function Resolve-LogsDir([int]$p){
  $work = Resolve-Path (Join-Path $SCRIPT_DIR '..\..\Work')
  $cands = Get-ChildItem -LiteralPath $work -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*_$p" }
  $chosen = $null
  if($cands){ $chosen = ($cands | Where-Object { $_.Name -notlike 'Project_*' } | Select-Object -First 1); if(-not $chosen){ $chosen = $cands | Select-Object -First 1 } }
  if(-not $chosen){ $chosen = New-Item -ItemType Directory -Path (Join-Path $work ("Project_{0}" -f $p)) }
  $logs = Join-Path $chosen.FullName 'Logs'
  if(-not (Test-Path $logs)){ New-Item -ItemType Directory -Path $logs | Out-Null }
  return $logs
}
$LOGS = Resolve-LogsDir -p $Port
if(-not $ViewId){
  $bootPath = Join-Path $LOGS 'agent_bootstrap.json'
  if(!(Test-Path $bootPath)){
    # fallback to Manuals/Logs for compatibility
    $bootPath = Join-Path (Resolve-Path (Join-Path $SCRIPT_DIR '..\Logs')) 'agent_bootstrap.json'
  }
  if(!(Test-Path $bootPath)){
    Write-Error "agent_bootstrap.json not found. Run test_connection.ps1 first."; exit 2
  }
  $boot = Get-Content $bootPath -Raw | ConvertFrom-Json
  try { $ViewId = [int]$boot.result.result.environment.activeViewId } catch { Write-Error "Could not read activeViewId from agent_bootstrap.json (expected at result.result.environment.activeViewId)."; exit 2 }
}
if($ViewId -le 0){ Write-Error "Invalid viewId=$ViewId. Must be a positive integer."; exit 2 }
chcp 65001 > $null
$env:PYTHONUTF8='1'
$json = '{"viewId":'+$ViewId+',"_shape":{"idsOnly":true,"page":{"limit":200}}}'
if($useEnv){ Write-Host "[Port] Using REVIT_MCP_PORT=$Port" -ForegroundColor DarkCyan }
Write-Host "[get_elements_in_view] viewId=$ViewId -> $LOGS" -ForegroundColor Cyan
python $PY --port $Port --command get_elements_in_view --params $json --output-file (Join-Path $LOGS 'elements_in_view.json')

