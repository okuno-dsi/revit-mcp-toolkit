param(
  [int]$Port = 5210,
  [string]$ProjectName = 'SS7Count',
  [int]$ViewId
)

$ErrorActionPreference = 'Stop'

$useEnv = $false
if(-not $PSBoundParameters.ContainsKey('Port') -and $env:REVIT_MCP_PORT){
  try { $Port = [int]$env:REVIT_MCP_PORT; $useEnv = $true } catch {}
}

chcp 65001 > $null
$env:PYTHONUTF8='1'

$SCRIPT_DIR = $PSScriptRoot
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'
if(!(Test-Path $PY)) { Write-Error "Python client not found: $PY"; exit 2 }

function Ensure-ProjectDir([string]$baseName, [int]$p){
  $workRoot = Resolve-Path (Join-Path $SCRIPT_DIR '..\..\Work')
  $projName = ("{0}_{1}" -f $baseName, $p)
  $projDir = Join-Path $workRoot $projName
  if(!(Test-Path $projDir)){ [void](New-Item -ItemType Directory -Path $projDir) }
  $logs = Join-Path $projDir 'Logs'
  if(!(Test-Path $logs)){ [void](New-Item -ItemType Directory -Path $logs) }
  $reports = Join-Path $projDir 'Reports'
  if(!(Test-Path $reports)){ [void](New-Item -ItemType Directory -Path $reports) }
  return @{ Root = $projDir; Logs = $logs; Reports = $reports }
}

function Get-Payload($jsonObj){
  if($null -ne $jsonObj.result){
    if($null -ne $jsonObj.result.result){ return $jsonObj.result.result }
    return $jsonObj.result
  }
  return $jsonObj
}

function Invoke-Revit($method, $paramsObj, $outFile){
  $paramsJson = if($null -ne $paramsObj) { ($paramsObj | ConvertTo-Json -Depth 20 -Compress) } else { '{}' }
  python $PY --port $Port --command $method --params $paramsJson --output-file $outFile | Out-Null
  if(!(Test-Path $outFile)){ throw "Expected output file not found: $outFile" }
  return (Get-Content -Raw -Encoding UTF8 -Path $outFile | ConvertFrom-Json)
}

if($useEnv){ Write-Host "[Port] Using REVIT_MCP_PORT=$Port" -ForegroundColor DarkCyan }
$dirs = Ensure-ProjectDir -baseName $ProjectName -p $Port
Write-Host ("[Dirs] Using {0}" -f $dirs.Root) -ForegroundColor DarkCyan

# 1) Resolve viewId
if(-not $ViewId -or $ViewId -le 0){
  $cvPath = Join-Path $dirs.Logs 'current_view.json'
  $cv = Invoke-Revit -method 'get_current_view' -paramsObj @{ } -outFile $cvPath
  $ViewId = [int](Get-Payload $cv).viewId
}
if($ViewId -le 0){ Write-Error "Invalid viewId=$ViewId"; exit 2 }
Write-Host ("[View] Active viewId={0}" -f $ViewId) -ForegroundColor Cyan

# 2) Collect elements in view (rows shape with paging)
$rowsAll = New-Object System.Collections.Generic.List[Object]
$skip = 0; $count = 1000; $guard = 0
while($true){
  $p = @{ viewId = $ViewId; skip = $skip; count = $count }
  $out = Join-Path $dirs.Logs ("elements_in_view_rows_{0}_{1}.json" -f $skip, $count)
  $resp = Invoke-Revit -method 'get_elements_in_view' -paramsObj $p -outFile $out
  $b = Get-Payload $resp
  if($b.rows){
    $rows = @($b.rows)
    if($rows.Count -eq 0){ break }
    foreach($r in $rows){ [void]$rowsAll.Add($r) }
    $skip += $count
    if($b.totalCount -and $rowsAll.Count -ge $b.totalCount){ break }
  } elseif($b.elementIds){
    foreach($id in $b.elementIds){ [void]$rowsAll.Add([ordered]@{ elementId = [int]$id }) }
    break
  } else {
    throw "Unexpected shape for get_elements_in_view response."
  }
  if((++$guard) -ge 100){ break }
}

if($rowsAll.Count -eq 0){ Write-Error "No elements found in the current view."; exit 3 }
Write-Host ("[View] Elements collected: {0}" -f $rowsAll.Count) -ForegroundColor Gray

$rowsPath = Join-Path $dirs.Logs 'elements_in_view_rows.json'
($rowsAll | ConvertTo-Json -Depth 10) | Out-File -FilePath $rowsPath -Encoding utf8

# Index by elementId for category mapping
$catByEid = @{}
foreach($r in $rowsAll){
  $eid = $r.elementId
  if($null -ne $eid){
    $catByEid[[int]$eid] = [ordered]@{
      categoryId = $r.categoryId
      categoryName = $r.categoryName
    }
  }
}

# 3) Bulk fetch instance parameters (Comments) and familyName
$allIds = @($rowsAll | ForEach-Object { if($_.elementId){ [int]$_.elementId } } | Sort-Object -Unique)

$batchSize = 400
$items = New-Object System.Collections.Generic.List[Object]
for($i=0; $i -lt $allIds.Count; $i += $batchSize){
  $batch = $allIds[$i..([Math]::Min($i+$batchSize-1, $allIds.Count-1))]
  $paramsObj = @{ elementIds = $batch; paramKeys = @(@{ name = 'Comments' }, @{ name = 'コメント' }) }
  $out = Join-Path $dirs.Logs ("inst_params_{0}_{1}.json" -f $i, $batch.Count)
  $resp = Invoke-Revit -method 'get_instance_parameters_bulk' -paramsObj $paramsObj -outFile $out
  $b = Get-Payload $resp
  if($b.items){ foreach($it in @($b.items)){ [void]$items.Add($it) } }
}

if($items.Count -eq 0){ Write-Error "No instance parameters returned (get_instance_parameters_bulk)."; exit 3 }

# 4) Aggregate by Category and Family
function Get-String([object]$o){ if($null -eq $o){ return '' } return [string]$o }

$groups = @{}
foreach($it in $items){
  $eid = [int]$it.elementId
  $fam = Get-String $it.familyName
  if([string]::IsNullOrWhiteSpace($fam) -and $it.typeName){ $fam = Get-String $it.typeName }
  if([string]::IsNullOrWhiteSpace($fam)){ $fam = '(Unknown)' }
  $catInfo = $catByEid[$eid]
  $cat = if($catInfo -and $catInfo.categoryName){ Get-String $catInfo.categoryName } else { Get-String $it.category }
  if([string]::IsNullOrWhiteSpace($cat) -and $catInfo -and $catInfo.categoryId){ $cat = ("cat:{0}" -f $catInfo.categoryId) }
  if([string]::IsNullOrWhiteSpace($cat)){ $cat = '(UnknownCategory)' }

  $p = $it.params
  $d = $it.display
  $comments = ''
  if($p){
    $pnames = @(); try { $pnames = $p.PSObject.Properties.Name } catch {}
    if($pnames -contains 'Comments'){ $comments = Get-String ($p | Select-Object -ExpandProperty Comments) }
    elseif($pnames -contains 'コメント'){ $comments = Get-String ($p | Select-Object -ExpandProperty コメント) }
  }
  if([string]::IsNullOrWhiteSpace($comments) -and $d){
    $dnames = @(); try { $dnames = $d.PSObject.Properties.Name } catch {}
    if($dnames -contains 'Comments'){ $comments = Get-String ($d | Select-Object -ExpandProperty Comments) }
    elseif($dnames -contains 'コメント'){ $comments = Get-String ($d | Select-Object -ExpandProperty コメント) }
  }
  $has = $false
  if(-not [string]::IsNullOrWhiteSpace($comments)){
    $has = ($comments -like '*SS7*') -or ($comments.ToLowerInvariant().Contains('ss7'))
  }

  if(-not $groups.ContainsKey($cat)){ $groups[$cat] = @{} }
  if(-not $groups[$cat].ContainsKey($fam)){
    $groups[$cat][$fam] = [ordered]@{ withSS7 = 0; withoutSS7 = 0; total = 0 }
  }
  $g = $groups[$cat][$fam]
  $g.total = [int]$g.total + 1
  if($has){ $g.withSS7 = [int]$g.withSS7 + 1 } else { $g.withoutSS7 = [int]$g.withoutSS7 + 1 }
}

# 5) Save JSON and CSV
$summary = [ordered]@{
  ok = $true
  port = $Port
  viewId = $ViewId
  projectDir = $dirs.Root
  totals = @{
    elementsInView = $allIds.Count
  }
  groups = $groups
}

$jsonPath = Join-Path $dirs.Reports 'ss7_by_category_family.json'
$csvPath = Join-Path $dirs.Reports 'ss7_by_category_family.csv'

$summary | ConvertTo-Json -Depth 100 | Out-File -FilePath $jsonPath -Encoding utf8

# Flatten for CSV
$rowsCsv = New-Object System.Collections.Generic.List[Object]
foreach($cat in ($groups.Keys | Sort-Object)){
  foreach($fam in ($groups[$cat].Keys | Sort-Object)){
    $g = $groups[$cat][$fam]
    $rowsCsv.Add([pscustomobject]@{
      Category = $cat
      Family = $fam
      WithSS7 = [int]$g.withSS7
      WithoutSS7 = [int]$g.withoutSS7
      Total = [int]$g.total
    }) | Out-Null
  }
}
$rowsCsv | Export-Csv -Path $csvPath -NoTypeInformation -Encoding utf8BOM

Write-Host ("Saved JSON: {0}" -f $jsonPath) -ForegroundColor Green
Write-Host ("Saved CSV : {0}" -f $csvPath) -ForegroundColor Green

# 6) Print compact summary
Write-Host "\n[Summary] SS7 in Comments by Category / Family" -ForegroundColor Cyan
foreach($cat in ($groups.Keys | Sort-Object)){
  Write-Host ("- {0}" -f $cat) -ForegroundColor Yellow
  foreach($fam in ($groups[$cat].Keys | Sort-Object)){
    $g = $groups[$cat][$fam]
    Write-Host ("    {0} : SS7={1}, Non-SS7={2}, Total={3}" -f $fam, $g.withSS7, $g.withoutSS7, $g.total)
  }
}
