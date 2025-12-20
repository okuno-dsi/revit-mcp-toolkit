param(
  [int]$Port = 5210,
  [switch]$IdsOnly
)

$useEnv = $false
if(-not $PSBoundParameters.ContainsKey('Port') -and $env:REVIT_MCP_PORT){
  try { $Port = [int]$env:REVIT_MCP_PORT; $useEnv = $true } catch {}
}

$SCRIPT_DIR = $PSScriptRoot
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'
$LOGS = Resolve-Path (Join-Path $SCRIPT_DIR '..\\Logs')

chcp 65001 > $null
$env:PYTHONUTF8='1'

$params = $null
if($IdsOnly){
  $params = '{"_shape":{"idsOnly":true}}'
}

if($useEnv){ Write-Host "[Port] Using REVIT_MCP_PORT=$Port" -ForegroundColor DarkCyan }
Write-Host "[get_levels]" -ForegroundColor Cyan

if($params){
  python $PY --port $Port --command get_levels --params $params --output-file (Join-Path $LOGS 'levels.json')
} else {
  python $PY --port $Port --command get_levels --output-file (Join-Path $LOGS 'levels.json')
}


