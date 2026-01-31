# @feature: make structural frame signs report | keywords: 梁, スペース, ビュー
param(
  [int]$Port = 5210,
  [string]$OutCsv
)

$ErrorActionPreference = 'Stop'
try { chcp 65001 > $null } catch {}
$env:PYTHONUTF8='1'

$SCRIPT_DIR = $PSScriptRoot
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'
if(!(Test-Path $PY)){ Write-Error "Python client not found: $PY"; exit 2 }

function Resolve-LogsDir([int]$p){
  $workRoot = Resolve-Path (Join-Path $SCRIPT_DIR '..\..\Work')
  $cands = Get-ChildItem -LiteralPath $workRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*_$p" }
  $chosen = $null
  if($cands){ $chosen = ($cands | Where-Object { $_.Name -notlike 'Project_*' } | Select-Object -First 1); if(-not $chosen){ $chosen = $cands | Select-Object -First 1 } }
  if(-not $chosen){ $chosen = New-Item -ItemType Directory -Path (Join-Path $workRoot ("Project_{0}" -f $p)) }
  $logs = Join-Path $chosen.FullName 'Logs'
  if(-not (Test-Path $logs)){ New-Item -ItemType Directory -Path $logs | Out-Null }
  return $logs
}

$LOGS = Resolve-LogsDir -p $Port
if(-not $OutCsv -or [string]::IsNullOrWhiteSpace($OutCsv)){
  $OutCsv = Join-Path $LOGS "structural_frame_signs_report_${Port}.csv"
}

function Invoke-Revit($method, $paramsObj, $outFile){
  $paramsJson = if($null -ne $paramsObj) { ($paramsObj | ConvertTo-Json -Depth 25 -Compress) } else { '{}' }
  python $PY --port $Port --command $method --params $paramsJson --output-file $outFile | Out-Null
  if(!(Test-Path $outFile)){ throw "Expected output file not found: $outFile" }
  return (Get-Content -Raw -Encoding UTF8 -Path $outFile | ConvertFrom-Json)
}

function Get-Payload($o){ if($o -and $o.result -and $o.result.result){ return $o.result.result } elseif($o -and $o.result){ return $o.result } else { return $o } }

Write-Host "[1/2] Fetching structural frame types ..." -ForegroundColor Cyan
$typesPath = Join-Path $LOGS 'structural_frame_types.json'
$typesObj = Invoke-Revit -method 'get_structural_frame_types' -paramsObj @{ } -outFile $typesPath
$typesPayload = Get-Payload $typesObj
$types = @()
if($typesPayload.types){ $types = @($typesPayload.types) }
elseif($typesPayload.items){ $types = @($typesPayload.items) }
elseif($typesPayload -is [System.Collections.IEnumerable]){ $types = @($typesPayload) }
if($types.Count -eq 0){ Write-Error "No structural frame types found (get_structural_frame_types)."; exit 3 }

Write-Host ("  types found = {0}" -f $types.Count) -ForegroundColor Gray

Write-Host "[2/2] Collecting two '符号' values per type ..." -ForegroundColor Cyan
$rows = @()
foreach($t in $types){
  $typeId = $null; $familyName = $null; $typeName = $null
  if($t.PSObject.Properties.Name -contains 'typeId'){ $typeId = [int]$t.typeId } elseif($t.PSObject.Properties.Name -contains 'id'){ $typeId = [int]$t.id }
  if($t.PSObject.Properties.Name -contains 'familyName'){ $familyName = ""+$t.familyName }
  if($t.PSObject.Properties.Name -contains 'typeName'){ $typeName = ""+$t.typeName } elseif($t.PSObject.Properties.Name -contains 'name'){ $typeName = ""+$t.name }
  if(-not $typeId -or $typeId -le 0){ continue }

  $outPath = Join-Path $LOGS ("type_{0}_params.json" -f $typeId)
  $resp = $null
  try { $resp = Invoke-Revit -method 'get_structural_frame_type_parameters' -paramsObj @{ typeId = $typeId } -outFile $outPath }
  catch { try { $resp = Invoke-Revit -method 'get_family_type_parameters' -paramsObj @{ typeId = $typeId } -outFile $outPath } catch { $resp = $null } }
  if(-not $resp){ continue }
  $payload = Get-Payload $resp

  $signA = '' ; $signB = '' ; $signAId = '' ; $signBId = ''
  $parArr = @()
  if($payload.parameters){ $parArr = @($payload.parameters) }
  elseif($payload.params){
    # params as dictionary
    foreach($k in $payload.params.PSObject.Properties.Name){ if($k -like '*符号*'){ if($signA -eq ''){ $signA = ""+$payload.params.$k; $signAId = $k } else { $signB = ""+$payload.params.$k; $signBId = $k } } }
  }
  if($parArr.Count -gt 0){
    foreach($p in $parArr){ if(($p.name -eq '符号') -or ($p.name -like '*符号*')){ if($signA -eq ''){ $signA = ""+$p.value; $signAId = ""+$p.id } else { $signB = ""+$p.value; $signBId = ""+$p.id } } }
  }

  $rows += [pscustomobject]@{
    Category = '構造フレーム'
    FamilyName = $familyName
    TypeName = $typeName
    TypeId = $typeId
    符号_5633006 = if($signAId -eq '5633006'){ $signA } elseif($signBId -eq '5633006'){ $signB } else { if($signAId -eq '' -and $signA -ne ''){ $signA } else { '' } }
    符号_5633379 = if($signAId -eq '5633379'){ $signA } elseif($signBId -eq '5633379'){ $signB } else { if($signBId -eq '' -and $signB -ne ''){ $signB } else { '' } }
  }
}

if($rows.Count -eq 0){ Write-Error "No rows collected. Could not find '符号' on types."; exit 4 }

$rows | Sort-Object FamilyName,TypeName | Export-Csv -Path $OutCsv -Encoding UTF8 -NoTypeInformation
Write-Host ("Saved report: {0}" -f $OutCsv) -ForegroundColor Green

# Also echo a small preview (first 10)
$rows | Select-Object -First 10 | Format-Table -AutoSize

