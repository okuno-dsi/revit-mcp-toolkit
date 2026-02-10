# @feature: copy sign typeparams | keywords: 梁, スペース
param(
  [int]$Port = 5210,
  [switch]$WhatIf,
  [int]$OpTimeoutMs = 180000
)

$ErrorActionPreference = 'Stop'
try { chcp 65001 > $null } catch {}
$env:PYTHONUTF8='1'

$SCRIPT_DIR = $PSScriptRoot
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'
if(!(Test-Path $PY)){ Write-Error "Python client not found: $PY"; exit 2 }

function Invoke-Revit($method, $paramsObj, $outFile){
  if($OpTimeoutMs -gt 0){ $paramsObj['opTimeoutMs'] = $OpTimeoutMs }
  $paramsJson = if($null -ne $paramsObj) { ($paramsObj | ConvertTo-Json -Depth 30 -Compress) } else { '{}' }
  python $PY --port $Port --command $method --params $paramsJson --output-file $outFile | Out-Null
  if(!(Test-Path $outFile)){ throw "Expected output file not found: $outFile" }
  return (Get-Content -Raw -Encoding UTF8 -Path $outFile | ConvertFrom-Json)
}

function Get-Payload($o){ if($o -and $o.result -and $o.result.result){ return $o.result.result } elseif($o -and $o.result){ return $o.result } else { return $o } }

# Logs dir
$workRoot = Resolve-Path (Join-Path $SCRIPT_DIR '..\\..\\..\\Projects')
$cands = Get-ChildItem -LiteralPath $workRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*_$Port" }
if(-not $cands){ $proj = New-Item -ItemType Directory -Path (Join-Path $workRoot ("Project_{0}" -f $Port)) } else { $proj = $cands | Select-Object -First 1 }
$logs = Join-Path $proj.FullName 'Logs'
if(-not (Test-Path $logs)){ New-Item -ItemType Directory -Path $logs | Out-Null }

Write-Host "[1/3] Fetching structural frame types..." -ForegroundColor Cyan
$typesPath = Join-Path $logs 'structural_frame_types.json'
$typesObj = Invoke-Revit -method 'get_structural_frame_types' -paramsObj @{ } -outFile $typesPath
$types = Get-Payload $typesObj
if($types.types){ $types = $types.types } elseif($types.items){ $types = $types.items }
if(-not $types){ Write-Error "No structural frame types found."; exit 3 }

Write-Host ("  types: {0}" -f $types.Count) -ForegroundColor Gray

$changed = @()
$skipped = @()

foreach($t in $types){
  $typeId = $null; $familyName = $null; $typeName = $null
  if($t.PSObject.Properties.Name -contains 'typeId'){ $typeId = [int]$t.typeId } elseif($t.PSObject.Properties.Name -contains 'id'){ $typeId = [int]$t.id }
  if($t.PSObject.Properties.Name -contains 'familyName'){ $familyName = ""+$t.familyName }
  if($t.PSObject.Properties.Name -contains 'typeName'){ $typeName = ""+$t.typeName } elseif($t.PSObject.Properties.Name -contains 'name'){ $typeName = ""+$t.name }
  if(-not $typeId -or $typeId -le 0){ continue }

  $pOut = Join-Path $logs ("type_{0}_params_for_sign.json" -f $typeId)
  $pObj = Invoke-Revit -method 'get_family_type_parameters' -paramsObj @{ typeId = $typeId } -outFile $pOut
  $payload = Get-Payload $pObj
  $pars = @()
  if($payload.parameters){ $pars = @($payload.parameters) }
  if($pars.Count -eq 0){ $skipped += $typeId; continue }

  # pick two '符号'
  $signs = $pars | Where-Object { $_.name -eq '符号' }
  if($signs.Count -lt 2){ $skipped += $typeId; continue }

  # Try id-based match first
  $a = $signs | Where-Object { $_.id -eq 5633006 } | Select-Object -First 1
  $b = $signs | Where-Object { $_.id -eq 5633379 } | Select-Object -First 1
  if(-not $a){ $a = $signs[0] }
  if(-not $b){ $b = $signs | Where-Object { $_ -ne $a } | Select-Object -First 1 }

  $valA = "" + ($a.value)
  $valB = "" + ($b.value)

  if([string]::IsNullOrWhiteSpace($valA) -and [string]::IsNullOrWhiteSpace($valB)){
    $skipped += $typeId; continue
  }

  if([string]::IsNullOrWhiteSpace($valA) -and -not [string]::IsNullOrWhiteSpace($valB)){
    Write-Host ("[COPY] {0} : 符号A <= 符号B ({1})" -f $typeName, $valB) -ForegroundColor Yellow
    if(-not $WhatIf){
      $guid = $a.guid
      $setOut = Join-Path $logs ("type_{0}_setA.json" -f $typeId)
      $paramPayload = @{ typeId = $typeId; value = $valB }
      if([string]::IsNullOrWhiteSpace($guid)){
        # fallback to name; ambiguous but try
        $paramPayload['paramName'] = '符号'
      } else {
        $paramPayload['guid'] = $guid
      }
      $r = Invoke-Revit -method 'set_family_type_parameter' -paramsObj $paramPayload -outFile $setOut
      $changed += $typeId
    }
  } elseif(-not [string]::IsNullOrWhiteSpace($valA) -and [string]::IsNullOrWhiteSpace($valB)){
    Write-Host ("[COPY] {0} : 符号B <= 符号A ({1})" -f $typeName, $valA) -ForegroundColor Yellow
    if(-not $WhatIf){
      $guid = $b.guid
      $setOut = Join-Path $logs ("type_{0}_setB.json" -f $typeId)
      $paramPayload = @{ typeId = $typeId; value = $valA }
      if([string]::IsNullOrWhiteSpace($guid)){
        $paramPayload['paramName'] = '符号'
      } else {
        $paramPayload['guid'] = $guid
      }
      $r = Invoke-Revit -method 'set_family_type_parameter' -paramsObj $paramPayload -outFile $setOut
      $changed += $typeId
    }
  } else {
    $skipped += $typeId
  }
}

Write-Host ("Done. Changed={0}, Skipped={1}" -f $changed.Count, $skipped.Count) -ForegroundColor Green



