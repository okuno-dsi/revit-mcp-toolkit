# @feature: compare selected instance parameters | keywords: misc
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
  $f = Get-ChildItem -Path $logs -Filter 'selected_instance_parameters.json' -Recurse -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if(-not $f){ throw "selected_instance_parameters.json not found under: $logs" }
  return $f.FullName
}

function Build-InstanceList($obj){
  $res = @()
  $items = @()
  if($obj -and $obj.instances){ $items = @($obj.instances) }
  foreach($it in $items){
    $params = $it.params; if(-not $params){ $params = @{} }
    $display = $it.display; if(-not $display){ $display = @{} }
    $res += [ordered]@{
      elementId = $it.elementId
      key = ("{0}|{1}|{2}|{3}" -f ($it.category), ($it.familyName), ($it.typeName), ($it.elementId))
      category = $it.category
      familyName = $it.familyName
      typeName = $it.typeName
      params = $params
      display = $display
    }
  }
  return ,$res
}

function Pair-Instances($A, $B){
  $pairs = @()
  $usedA = @(); for($i=0;$i -lt $A.Count;$i++){ $usedA += $false }
  $usedB = @(); for($j=0;$j -lt $B.Count;$j++){ $usedB += $false }

  # 1) exact same elementId
  for($i=0; $i -lt $A.Count; $i++){
    for($j=0; $j -lt $B.Count; $j++){
      if($usedA[$i] -or $usedB[$j]){ continue }
      if($A[$i].elementId -eq $B[$j].elementId){
        $pairs += @{ aIndex=$i; bIndex=$j; a=$A[$i]; b=$B[$j] }
        $usedA[$i]=$true; $usedB[$j]=$true
      }
    }
  }

  # 2) by category+family+type (first unmatched)
  for($i=0; $i -lt $A.Count; $i++){
    if($usedA[$i]){ continue }
    for($j=0; $j -lt $B.Count; $j++){
      if($usedB[$j]){ continue }
      if(($A[$i].category -eq $B[$j].category) -and ($A[$i].familyName -eq $B[$j].familyName) -and ($A[$i].typeName -eq $B[$j].typeName)){
        $pairs += @{ aIndex=$i; bIndex=$j; a=$A[$i]; b=$B[$j] }
        $usedA[$i]=$true; $usedB[$j]=$true; break
      }
    }
  }

  # 3) remaining by order
  $ia = 0; $ib = 0
  while($ia -lt $A.Count -and $ib -lt $B.Count){
    while($ia -lt $A.Count -and $usedA[$ia]){ $ia++ }
    while($ib -lt $B.Count -and $usedB[$ib]){ $ib++ }
    if($ia -lt $A.Count -and $ib -lt $B.Count){
      $pairs += @{ aIndex=$ia; bIndex=$ib; a=$A[$ia]; b=$B[$ib] }
      $usedA[$ia]=$true; $usedB[$ib]=$true
      $ia++; $ib++
    }
  }

  return @{ pairs=$pairs; usedA=$usedA; usedB=$usedB }
}

function Compare-Params($left, $right){
  $pnames = New-Object System.Collections.Generic.HashSet[string]
  foreach($n in $left.params.Keys){ [void]$pnames.Add([string]$n) }
  foreach($n in $right.params.Keys){ [void]$pnames.Add([string]$n) }
  $diffParams = @()
  foreach($n in $pnames){
    $lv = $left.params[$n]
    $rv = $right.params[$n]
    $eq = $false
    if($null -eq $lv -and $null -eq $rv){ $eq = $true }
    elseif($lv -is [double] -and $rv -is [double]){ $eq = ([math]::Abs($lv - $rv) -lt 1e-9) }
    else { $eq = ("$lv" -eq "$rv") }
    if(-not $eq){
      $diffParams += [ordered]@{
        name = $n
        left = @{ value = $lv; display = ($left.display[$n]) }
        right = @{ value = $rv; display = ($right.display[$n]) }
      }
    }
  }
  return $diffParams
}

Write-Host "[Compare Instances] Loading A=$PortA, B=$PortB ..." -ForegroundColor Cyan
$workA = Resolve-WorkDir -p $PortA
$workB = Resolve-WorkDir -p $PortB
$fileA = Resolve-Json -work $workA
$fileB = Resolve-Json -work $workB

$objA = Get-Content -Raw -Encoding UTF8 -Path $fileA | ConvertFrom-Json
$objB = Get-Content -Raw -Encoding UTF8 -Path $fileB | ConvertFrom-Json

$listA = Build-InstanceList $objA
$listB = Build-InstanceList $objB

$pairRes = Pair-Instances -A $listA -B $listB
$pairs = $pairRes.pairs

$diffDetails = @()
foreach($p in $pairs){
  $a = $p.a; $b = $p.b
  $dp = Compare-Params -left $a -right $b
  if($dp.Count -gt 0){
    $diffDetails += [ordered]@{
      a = @{ port = $PortA; elementId = $a.elementId; category = $a.category; familyName = $a.familyName; typeName = $a.typeName }
      b = @{ port = $PortB; elementId = $b.elementId; category = $b.category; familyName = $b.familyName; typeName = $b.typeName }
      params = $dp
    }
  }
}

$onlyA = @()
for($i=0; $i -lt $listA.Count; $i++){ if(-not $pairRes.usedA[$i]){ $onlyA += $listA[$i] } }
$onlyB = @()
for($j=0; $j -lt $listB.Count; $j++){ if(-not $pairRes.usedB[$j]){ $onlyB += $listB[$j] } }

$outDir = Join-Path $workA 'Logs'
$outFile = Join-Path $outDir ("selected_instance_parameters_diff_{0}_vs_{1}.json" -f $PortA, $PortB)
([ordered]@{ ok = $true; portA = $PortA; portB = $PortB; workA = $workA; workB = $workB; summary = @{ countA = $listA.Count; countB = $listB.Count; paired = $pairs.Count; changed = $diffDetails.Count; onlyA = $onlyA.Count; onlyB = $onlyB.Count }; details = @{ pairs = $pairs; changed = $diffDetails; onlyA = $onlyA; onlyB = $onlyB } }) | ConvertTo-Json -Depth 100 | Out-File -FilePath $outFile -Encoding utf8

Write-Host "Saved diff: $outFile" -ForegroundColor Green
Write-Host ("Paired: {0}, Changed: {1}, OnlyA: {2}, OnlyB: {3}" -f ($pairs.Count), $diffDetails.Count, $onlyA.Count, $onlyB.Count) -ForegroundColor Yellow


