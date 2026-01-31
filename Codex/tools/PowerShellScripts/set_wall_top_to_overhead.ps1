# @feature: set wall top to overhead | keywords: 壁, ビュー
param(
  [int]$Port = 5210,
  [ValidateSet('auto','attach','raycast')]
  [string]$Mode = 'auto',
  [ValidateSet('wallTop','wallBase','top','base')]
  [string]$StartFrom = 'wallTop',
  [int[]]$WallIds,
  [int]$View3dId = 0,
  [int[]]$CategoryIds,
  [switch]$IncludeLinked,
  [switch]$DryRun,
  [int]$WaitSec = 180,
  [int]$JobTimeoutSec = 240
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8 = '1'

$PY = Join-Path $PSScriptRoot 'send_revit_command_durable.py'
if(!(Test-Path $PY)) { throw "Python client not found: $PY" }

$params = @{
  mode = $Mode
  startFrom = $StartFrom
  dryRun = [bool]$DryRun
  apply = (-not $DryRun)
  __smoke_ok = $true
}
if($WallIds -and $WallIds.Count -gt 0){ $params.wallIds = @($WallIds | ForEach-Object { [int]$_ }) }
if($View3dId -gt 0){ $params.view3dId = [int]$View3dId }
if($CategoryIds -and $CategoryIds.Count -gt 0){ $params.categoryIds = @($CategoryIds | ForEach-Object { [int]$_ }) }
if($IncludeLinked.IsPresent){ $params.includeLinked = $true }

$pjson = ($params | ConvertTo-Json -Depth 50 -Compress)
python -X utf8 $PY --port $Port --command set_wall_top_to_overhead --params $pjson --wait-seconds $WaitSec --timeout-sec $JobTimeoutSec
