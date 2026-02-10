# @feature: set visual override | keywords: 壁, ビュー
param(
  [int]$Port = 5210,
  [int]$ElementId,
  [int]$R = 255,
  [int]$G = 0,
  [int]$B = 0,
  [int]$Transparency = 60
)
$useEnv = $false
if(-not $PSBoundParameters.ContainsKey('Port') -and $env:REVIT_MCP_PORT){
  try { $Port = [int]$env:REVIT_MCP_PORT; $useEnv = $true } catch {}
}
$SCRIPT_DIR = $PSScriptRoot
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'
$LOGS = Resolve-Path (Join-Path $SCRIPT_DIR '..\Logs')
if(-not $ElementId){
  $evPath = Join-Path $LOGS 'elements_in_view.json'
  if(!(Test-Path $evPath)){
    Write-Error "elements_in_view.json not found at $evPath. Run list_elements_in_view.ps1 first or pass -ElementId explicitly."; exit 2
  }
  $data = Get-Content $evPath -Raw | ConvertFrom-Json
  $rows = $data.result.result.rows
  if(-not $rows){ Write-Error "No rows found in elements_in_view.json (expected at result.result.rows)."; exit 2 }
  $ElementId = [int](
    ($rows | Where-Object { $_.categoryName -eq '壁' -and $_.elementId -gt 0 } | Select-Object -First 1).elementId
  )
  if($ElementId -le 0){
    $ElementId = [int](($rows | Where-Object { $_.elementId -gt 0 } | Select-Object -First 1).elementId)
  }
}
if($ElementId -le 0){ Write-Error "Invalid elementId=$ElementId. Provide a valid element ID with -ElementId or refresh elements_in_view.json."; exit 2 }
chcp 65001 > $null
$env:PYTHONUTF8='1'
$json = '{"elementId":'+$ElementId+',"color":{"r":'+$R+',"g":'+$G+',"b":'+$B+'},"transparency":'+$Transparency+',"__smoke_ok":true}'
if($useEnv){ Write-Host "[Port] Using REVIT_MCP_PORT=$Port" -ForegroundColor DarkCyan }
Write-Host "[set_visual_override] elementId=$ElementId" -ForegroundColor Yellow
python $PY --port $Port --command set_visual_override --params $json --wait-seconds 120


