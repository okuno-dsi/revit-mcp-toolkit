# @feature: get family instance references for selection | keywords: スペース
param(
  [int]$Port = 5210,
  [int]$ElementId = 0,
  [string]$UniqueId = '',
  [string[]]$ReferenceTypes,
  [int]$MaxPerType = 50,
  [switch]$IncludeGeometry,
  [switch]$IncludeEmpty,
  [switch]$NoStable
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try { chcp 65001 > $null } catch {}
$env:PYTHONUTF8 = '1'

$PY = Join-Path $PSScriptRoot 'send_revit_command_durable.py'

$params = [ordered]@{
  includeStable = (-not [bool]$NoStable)
  includeGeometry = [bool]$IncludeGeometry
  includeEmpty = [bool]$IncludeEmpty
  maxPerType = $MaxPerType
}

if($ElementId -gt 0){ $params.elementId = $ElementId }
if(-not [string]::IsNullOrWhiteSpace($UniqueId)){ $params.uniqueId = $UniqueId }
if($ReferenceTypes -and $ReferenceTypes.Count -gt 0){ $params.referenceTypes = $ReferenceTypes }

$json = ($params | ConvertTo-Json -Depth 50 -Compress)
python -X utf8 $PY --port $Port --command get_family_instance_references --params $json --wait-seconds 240

