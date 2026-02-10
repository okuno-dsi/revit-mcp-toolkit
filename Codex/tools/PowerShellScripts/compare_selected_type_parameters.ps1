# @feature: compare selected type parameters | keywords: misc
param(
  [int]$PortA = 5210,
  [int]$PortB = 5211
)

$ErrorActionPreference = 'Stop'
chcp 65001 > $null

function Resolve-WorkDir([int]$p){
  $root = Resolve-Path (Join-Path $PSScriptRoot '..\\..\\..\\Projects')
  $dirs = Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*_$p" }
  $chosen = $null
  if($dirs){ $chosen = ($dirs | Where-Object { $_.Name -notlike 'Project_*' } | Select-Object -First 1); if(-not $chosen){ $chosen = $dirs | Select-Object -First 1 } }
  if(-not $chosen){ $chosen = New-Item -ItemType Directory -Path (Join-Path $root ("Project_{0}" -f $p)) }
  return $chosen.FullName
}

function Resolve-Json([string]$work){
  $logs = Join-Path $work 'Logs'
  if(!(Test-Path $logs)){ throw "Logs folder not found: $logs" }
  $f = Get-ChildItem -Path $logs -Filter 'selected_type_parameters.json' -Recurse -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if(-not $f){ throw "selected_type_parameters.json not found under: $logs" }
  return $f.FullName
}

function Extract-TypeMap($obj){
  $map = @{}
  $types = @()
  if($obj -and $obj.types){ $types = @($obj.types) }
  foreach($t in $types){
    $key = "{0}|{1}|{2}" -f ($t.category), ($t.familyName), ($t.typeName)
    $payload = $t.result
    $params = @{}
    $display = @{}
    if($payload){
      if($payload.params){ $params = $payload.params }
      elseif($payload.result -and $payload.result.params){ $params = $payload.result.params }
      if($payload.display){ $display = $payload.display }
      elseif($payload.result -and $payload.result.display){ $display = $payload.result.display }
      # Handle array-shaped payloads like { parameters:[{name,value,displayValue}...] }
      if((!$params -or $params.Count -eq 0) -and $payload.parameters){
        foreach($p in $payload.parameters){
          $n = $p.name
          if($null -ne $n){
            $params[$n] = $p.value
            if($p.displayValue){ $display[$n] = $p.displayValue }
          }
        }
      }
    }
    $map[$key] = [ordered]@{ meta = $t; params = $params; display = $display }
  }
  return $map
}

function Compare-ParamMaps($left, $right){
  $result = [ordered]@{ different = @(); onlyLeft = @(); onlyRight = @() }
  $keys = New-Object System.Collections.Generic.HashSet[string]
  foreach($k in $left.Keys){ [void]$keys.Add($k) }
  foreach($k in $right.Keys){ [void]$keys.Add($k) }
  foreach($k in $keys){
    $l = $left[$k]
    $r = $right[$k]
    if($null -eq $l){ $result.onlyRight += $k; continue }
    if($null -eq $r){ $result.onlyLeft += $k; continue }
    # Compare params by union of names
    $pnames = New-Object System.Collections.Generic.HashSet[string]
    foreach($n in $l.params.Keys){ [void]$pnames.Add([string]$n) }
    foreach($n in $r.params.Keys){ [void]$pnames.Add([string]$n) }
    $diffParams = @()
    foreach($n in $pnames){
      $lv = $l.params[$n]
      $rv = $r.params[$n]
      $eq = $false
      if($null -eq $lv -and $null -eq $rv){ $eq = $true }
      elseif($lv -is [double] -and $rv -is [double]){ $eq = ([math]::Abs($lv - $rv) -lt 1e-9) }
      else { $eq = ("$lv" -eq "$rv") }
      if(-not $eq){
        $diffParams += [ordered]@{
          name = $n
          left = @{ value = $lv; display = ($l.display[$n]) }
          right = @{ value = $rv; display = ($r.display[$n]) }
        }
      }
    }
    if($diffParams.Count -gt 0){
      $result.different += [ordered]@{ key = $k; left = $l.meta; right = $r.meta; params = $diffParams }
    }
  }
  return $result
}

Write-Host "[Compare] Loading A=$PortA, B=$PortB ..." -ForegroundColor Cyan
$workA = Resolve-WorkDir -p $PortA
$workB = Resolve-WorkDir -p $PortB
$fileA = Resolve-Json -work $workA
$fileB = Resolve-Json -work $workB

$objA = Get-Content -Raw -Encoding UTF8 -Path $fileA | ConvertFrom-Json
$objB = Get-Content -Raw -Encoding UTF8 -Path $fileB | ConvertFrom-Json

$mapA = Extract-TypeMap $objA
$mapB = Extract-TypeMap $objB

$diff = Compare-ParamMaps -left $mapA -right $mapB

$outDir = Join-Path $workA 'Logs'
$outFile = Join-Path $outDir ("selected_type_parameters_diff_{0}_vs_{1}.json" -f $PortA, $PortB)
([ordered]@{ ok = $true; portA = $PortA; portB = $PortB; workA = $workA; workB = $workB; summary = @{ onlyA = $diff.onlyLeft.Count; onlyB = $diff.onlyRight.Count; changed = $diff.different.Count }; details = $diff }) | ConvertTo-Json -Depth 100 | Out-File -FilePath $outFile -Encoding utf8

Write-Host "Saved diff: $outFile" -ForegroundColor Green

# Print concise summary to console
Write-Host ("Types only in {0}: {1}" -f $PortA, ($diff.onlyLeft.Count)) -ForegroundColor Yellow
Write-Host ("Types only in {0}: {1}" -f $PortB, ($diff.onlyRight.Count)) -ForegroundColor Yellow
Write-Host ("Types changed: {0}" -f ($diff.different.Count)) -ForegroundColor Yellow

if($diff.different.Count -gt 0){
  Write-Host "--- Changed (first 5) ---" -ForegroundColor Gray
  $i = 0
  foreach($d in $diff.different){
    Write-Host ("* {0}" -f $d.key) -ForegroundColor Gray
    foreach($p in $d.params | Select-Object -First 5){
      Write-Host ("  - {0}: A={1} ({2}) | B={3} ({4})" -f $p.name, $p.left.value, $p.left.display, $p.right.value, $p.right.display)
    }
    $i++
    if($i -ge 5){ break }
  }
}



