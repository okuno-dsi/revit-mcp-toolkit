# @feature: save view workspace | keywords: スペース, ビュー
param(
  [int]$Port = 5210,
  [Nullable[bool]]$IncludeZoom,
  [Nullable[bool]]$Include3dOrientation,
  [int]$Retention
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

$params = @{}
if($PSBoundParameters.ContainsKey('IncludeZoom')){ $params.include_zoom = [bool]$IncludeZoom }
if($PSBoundParameters.ContainsKey('Include3dOrientation')){ $params.include_3d_orientation = [bool]$Include3dOrientation }
if($PSBoundParameters.ContainsKey('Retention')){ $params.retention = [int]$Retention }

chcp 65001 > $null
$env:PYTHONUTF8='1'

if($useEnv){ Write-Host "[Port] Using REVIT_MCP_PORT=$Port" -ForegroundColor DarkCyan }

$outPath = Join-Path $LOGS 'save_view_workspace.json'
Write-Host "[save_view_workspace] -> $outPath" -ForegroundColor Cyan
if($params.Count -gt 0){
  $json = ($params | ConvertTo-Json -Compress -Depth 6)
  python $PY --port $Port --command save_view_workspace --params $json --output-file $outPath
} else {
  python $PY --port $Port --command save_view_workspace --output-file $outPath
}



