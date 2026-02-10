# @feature: add door size dimensions in active view | keywords: スペース, ビュー
param(
  [int]$Port = 5210,
  [int]$ViewId = 0,
  [double]$OffsetMm = 200.0,
  [double[]]$WidthOffsetsMm,
  [double[]]$HeightOffsetsMm,
  [ValidateSet('top','bottom')][string]$WidthSide = 'top',
  [ValidateSet('left','right')][string]$HeightSide = 'left',
  [bool]$EnsureDimensionsVisible = $true,
  [bool]$KeepInsideCrop = $true,
  [double]$CropMarginMm = 30.0,
  [int]$TypeId = 0,
  [string]$TypeName = '',
  [int]$OverrideR = -1,
  [int]$OverrideG = -1,
  [int]$OverrideB = -1,
  [switch]$WidthOnly,
  [switch]$HeightOnly,
  [switch]$DetachViewTemplate,
  [switch]$Debug
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try { chcp 65001 > $null } catch {}
$env:PYTHONUTF8 = '1'

$PY = Join-Path $PSScriptRoot 'send_revit_command_durable.py'

$addWidth = $true
$addHeight = $true
if($WidthOnly -and -not $HeightOnly){ $addHeight = $false }
if($HeightOnly -and -not $WidthOnly){ $addWidth = $false }

$params = [ordered]@{
  offsetMm = $OffsetMm
  addWidth = $addWidth
  addHeight = $addHeight
  detachViewTemplate = [bool]$DetachViewTemplate
  ensureDimensionsVisible = [bool]$EnsureDimensionsVisible
  keepInsideCrop = [bool]$KeepInsideCrop
  cropMarginMm = $CropMarginMm
  debug = [bool]$Debug
}

if($ViewId -gt 0){ $params.viewId = $ViewId }
if($TypeId -gt 0){ $params.typeId = $TypeId }
if(-not [string]::IsNullOrWhiteSpace($TypeName)){ $params.typeName = $TypeName }
if($WidthSide){ $params.widthSide = $WidthSide }
if($HeightSide){ $params.heightSide = $HeightSide }
if($WidthOffsetsMm -and $WidthOffsetsMm.Count -gt 0){ $params.widthOffsetsMm = $WidthOffsetsMm }
if($HeightOffsetsMm -and $HeightOffsetsMm.Count -gt 0){ $params.heightOffsetsMm = $HeightOffsetsMm }
if($OverrideR -ge 0 -and $OverrideG -ge 0 -and $OverrideB -ge 0){
  $params.overrideRgb = @{ r = $OverrideR; g = $OverrideG; b = $OverrideB }
}

$json = ($params | ConvertTo-Json -Depth 50 -Compress)
python -X utf8 $PY --port $Port --command add_door_size_dimensions --params $json --wait-seconds 240

