param(
  [int]$Port = 5210,
  [int]$BatchSize = 800,
  [int]$MaxMillisPerTx = 3000,
  [int]$WaitSec = 360,
  [int]$JobTimeoutSec = 360,
  [switch]$Activate,
  [switch]$ReuseExisting
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8 = '1'
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

$PY = Join-Path $PSScriptRoot 'send_revit_command_durable.py'
$LOGS = Resolve-Path (Join-Path $PSScriptRoot '..\Logs')
$MANIFEST = Join-Path $LOGS 'create_seed_and_type_views.manifest.json'

function Invoke-Mcp {
  param([string]$Method,[hashtable]$Params,[int]$Wait=$WaitSec,[int]$JobSec=$JobTimeoutSec,[switch]$Force)
  $pjson = ($Params | ConvertTo-Json -Depth 100 -Compress)
  $args = @('--port', $Port, '--command', $Method, '--params', $pjson, '--wait-seconds', [string]$Wait)
  if($JobSec -gt 0){ $args += @('--timeout-sec', [string]$JobSec) }
  if($Force){ $args += '--force' }
  $tmp = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ("mcp_"+[System.IO.Path]::GetRandomFileName()+".json"))
  $args += @('--output-file', $tmp)
  $null = & python -X utf8 $PY @args 2>$null
  $code = $LASTEXITCODE
  $txt = ''
  try { $txt = Get-Content -LiteralPath $tmp -Raw -Encoding UTF8 } catch {}
  if(Test-Path $tmp){ Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
  if($code -ne 0){ throw "MCP call failed ($Method): $txt" }
  if([string]::IsNullOrWhiteSpace($txt)){ throw "Empty response from MCP ($Method)" }
  return ($txt | ConvertFrom-Json -Depth 400)
}

function Get-JsonPath($obj, [string[]]$paths){
  foreach($p in $paths){
    try{ $cur=$obj; foreach($seg in $p.Split('.')){ $cur = $cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; return $cur }catch{}
  }
  return $null
}

function Get-ActiveView(){
  $cv = Invoke-Mcp 'get_current_view' @{} 60 120 -Force
  $vid = 0; $name = ''
  foreach($p in 'result.result.viewId','result.viewId','viewId'){ try{ $cur=$cv; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $vid=[int]$cur; break }catch{} }
  foreach($p in 'result.result.name','result.name','name'){ try{ $cur=$cv; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $name=[string]$cur; break }catch{} }
  if($vid -le 0){ throw 'Could not resolve active viewId' }
  return @{ viewId=$vid; name=$name }
}

function Duplicate-View([int]$viewId,[string]$prefix){
  $dup = Invoke-Mcp 'duplicate_view' @{ viewId=$viewId; withDetailing=$true; namePrefix=$prefix; __smoke_ok=$true } 120 240 -Force
  foreach($p in 'result.result.viewId','result.viewId','viewId'){
    try{ $cur=$dup; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; return [int]$cur }catch{}
  }
  throw 'duplicate_view did not return viewId'
}

function Reset-View([int]$viewId){
  $idx = 0
  $lastIdx = -1
  do {
    # Lightweight reset: detach template + clear temp hide/isolate only. Avoid heavy per-element unhide/overrides.
    $params = @{ viewId=$viewId; detachViewTemplate=$true; includeTempReset=$true; unhideElements=$false; clearElementOverrides=$false; batchSize=$BatchSize; maxMillisPerTx=$MaxMillisPerTx; startIndex=$idx; refreshView=$true; __smoke_ok=$true }
    $r = Invoke-Mcp 'show_all_in_view' $params 300 300 -Force
    $nxt = $null
    foreach($p in 'result.result.nextIndex','result.nextIndex','nextIndex'){ try{ $cur=$r; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $nxt=$cur; break }catch{} }
    if($null -ne $nxt){ try{ $n = [int]$nxt }catch{ $n = 0 } } else { $n = 0 }
    if($n -le $idx -or $n -eq $lastIdx){ $idx = 0 } else { $lastIdx = $idx; $idx = $n }
  } while ($idx -gt 0)
}

function Get-IdsInView([int]$viewId,[int[]]$includeCatIds,[int[]]$excludeCatIds){
  $shape = @{ idsOnly = $true; page = @{ limit = 200000 } }
  $filter = @{}
  if($includeCatIds){ $filter['includeCategoryIds'] = @($includeCatIds) }
  if($excludeCatIds){ $filter['excludeCategoryIds'] = @($excludeCatIds) }
  $params = @{ viewId=$viewId; _shape=$shape; _filter=$filter }
  $res = Invoke-Mcp 'get_elements_in_view' $params 300 300 -Force
  foreach($path in 'result.result.elementIds','result.elementIds','elementIds'){
    try{ $cur=$res; foreach($seg in $path.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; return @($cur | ForEach-Object { [int]$_ }) }catch{}
  }
  return @()
}

function Hide-Elements-Batched([int]$viewId,[int[]]$ids){
  if(-not $ids -or $ids.Count -eq 0){ return }
  $chunk = $BatchSize
  for($i=0; $i -lt $ids.Count; $i+=$chunk){
    $hi = [Math]::Min($i+$chunk-1,$ids.Count-1)
    $batch = @($ids[$i..$hi])
    $params = @{ viewId=$viewId; elementIds=@($batch); detachViewTemplate=$true; refreshView=$true; batchSize=$BatchSize; maxMillisPerTx=$MaxMillisPerTx; __smoke_ok=$true }
    $null = Invoke-Mcp 'hide_elements_in_view' $params 300 300 -Force
  }
}

function Get-WallTypeGroups([int]$viewId){
  $WALL = -2000011
  $wallIds = Get-IdsInView -viewId $viewId -includeCatIds @($WALL) -excludeCatIds @()
  $groups = @{}
  if($wallIds.Count -eq 0){ return $groups }
  $chunk = 200
  for($i=0; $i -lt $wallIds.Count; $i+=$chunk){
    $hi = [Math]::Min($i+$chunk-1,$wallIds.Count-1)
    $batch = @($wallIds[$i..$hi])
    $info = Invoke-Mcp 'get_element_info' @{ elementIds=@($batch); rich=$true } 300 300 -Force
    $elems = @()
    foreach($p in 'result.result.elements','result.elements','elements'){ try{ $cur=$info; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $elems=@($cur); break }catch{} }
    foreach($e in $elems){
      $eid = 0; $tn = ''
      try{ $eid = [int]$e.elementId }catch{}
      if($eid -le 0){ continue }
      try{ $tn = [string]$e.typeName }catch{}
      if([string]::IsNullOrWhiteSpace($tn)){
        try{ $tn = [string]$e.symbol.name }catch{}
      }
      if([string]::IsNullOrWhiteSpace($tn)){
        try{ $tn = [string]$e.type.name }catch{}
      }
      if([string]::IsNullOrWhiteSpace($tn)){
        try{ $tn = [string]$e.parameters.'Type Name'.value }catch{}
      }
      if([string]::IsNullOrWhiteSpace($tn)){
        try{ $tn = [string]$e.parameters.'タイプ名'.value }catch{}
      }
      if([string]::IsNullOrWhiteSpace($tn)){ $tn = 'WT' }
      if(-not $groups.ContainsKey($tn)){ $groups[$tn] = New-Object System.Collections.Generic.List[int] }
      [void]$groups[$tn].Add($eid)
    }
  }
  return $groups
}

function Rename-View([int]$viewId,[string]$newName){
  # Server expects paramName; try EN then JP fallback
  $ok = $false
  foreach($pn in @('View Name','ビュー名')){
    try{
      $res = Invoke-Mcp 'set_view_parameter' @{ viewId=$viewId; paramName=$pn; value=$newName; __smoke_ok=$true } 60 120 -Force
      $jr = Get-JsonPath $res @('result.result','result')
      if($jr -and $jr.ok){ $ok = $true; break }
    } catch {}
  }
  return $ok
}

function Get-IdsInViewWithFilter([int]$viewId,[hashtable]$_filter,[switch]$IdsOnly){
  $shape = @{ idsOnly = ($IdsOnly -or $true); page = @{ limit = 200000 } }
  $params = @{ viewId=$viewId; _shape=$shape; _filter=$_filter }
  $res = Invoke-Mcp 'get_elements_in_view' $params 300 300 -Force
  foreach($path in 'result.result.elementIds','result.elementIds','elementIds','result.result.rows'){
    try{ $cur=$res; foreach($seg in $path.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop };
      if($path -like '*rows'){ return @($cur | ForEach-Object { try{ [int]$_.elementId }catch{0} } | Where-Object { $_ -gt 0 }) }
      return @($cur | ForEach-Object { [int]$_ })
    }catch{}
  }
  return @()
}

function Isolate-By-Type([int]$viewId,[string]$typeName,[int[]]$allWallIds,[hashtable]$groupsMap){
  # First try one-shot isolation
  $filter = @{ includeClasses=@('Wall'); includeCategoryIds=@(-2000011); parameterRules=@(@{ target='type'; builtInName='SYMBOL_NAME_PARAM'; op='eq'; value=$typeName }); logic='all' }
  $params = @{ viewId=$viewId; detachViewTemplate=$true; reset=$true; keepAnnotations=$true; batchSize=[Math]::Max(100,$BatchSize); filter=$filter; __smoke_ok=$true }
  try {
    $res = Invoke-Mcp 'isolate_by_filter_in_view' $params 300 300 -Force
    $jr = Get-JsonPath $res @('result.result','result')
    $k = 0; $h = 0; $t = 0
    try{ $k = [int]$jr.kept }catch{}
    try{ $h = [int]$jr.hidden }catch{}
    try{ $t = [int]$jr.total }catch{}
    Write-Host ("    [Isolate] kept={0} hidden={1} total={2}" -f $k,$h,$t) -ForegroundColor DarkGray
    if($jr -and $jr.ok -and ($k -gt 0 -or $h -gt 0)){ return $true }
  } catch {}

  # Fallback: Hide non-walls + walls of other types
  $nonwalls = Get-IdsInViewWithFilter -viewId $viewId -_filter @{ modelOnly=$true; excludeImports=$true; excludeCategoryIds=@(-2000011) } -IdsOnly
  $targetIds = @($groupsMap[$typeName])
  $otherWalls = @($allWallIds | Where-Object { $targetIds -notcontains $_ })
  $hideIds = @()
  if($nonwalls.Count){ $hideIds += $nonwalls }
  if($otherWalls.Count){ $hideIds += $otherWalls }
  if($hideIds.Count){ Hide-Elements-Batched -viewId $viewId -ids $hideIds }
  return $true
}

function Delete-Views-From-Manifest(){
  if(!(Test-Path $MANIFEST)){ return }
  try{
    $mf = Get-Content -LiteralPath $MANIFEST -Raw -Encoding UTF8 | ConvertFrom-Json -Depth 60
    $ids = @()
    if($mf.seedId){ $ids += [int]$mf.seedId }
    foreach($v in @($mf.typeViews)){
      try{ if($v.viewId){ $ids += [int]$v.viewId } }catch{}
    }
    $ids = @($ids | Sort-Object -Unique)
    foreach($vid in $ids){
      try{ $null = Invoke-Mcp 'delete_view' @{ viewId=$vid; __smoke_ok=$true } 120 240 -Force; Write-Host ("[Cleanup] Deleted viewId={0}" -f $vid) -ForegroundColor DarkYellow } catch {}
    }
  } catch {}
  try{ Remove-Item -LiteralPath $MANIFEST -Force -ErrorAction SilentlyContinue } catch {}
}

# --- Main ---
$active = Get-ActiveView
$activeId = [int]$active.viewId
Write-Host ("[Active] viewId={0} name='{1}'" -f $activeId, $active.name) -ForegroundColor Cyan

if(-not $ReuseExisting){ Delete-Views-From-Manifest }

# Seed view
Write-Host "[Seed] Preparing Seed view" -ForegroundColor Cyan
$seedId = $null
if($ReuseExisting -and (Test-Path $MANIFEST)){
  try{ $mf = Get-Content -LiteralPath $MANIFEST -Raw -Encoding UTF8 | ConvertFrom-Json -Depth 60; $seedId = [int]$mf.seedId }catch{}
}
if(-not $seedId){
  Write-Host "[Seed] Duplicating active view" -ForegroundColor Cyan
  $seedId = Duplicate-View -viewId $activeId -prefix ''
}
if($Activate){ try { $act = @{ viewId=$seedId } | ConvertTo-Json -Compress; & python $PY --port $Port --command activate_view --params $act --wait-seconds 30 2>$null | Out-Null } catch {} }
Write-Host ("[Seed] Resetting viewId={0}" -f $seedId) -ForegroundColor Cyan
Reset-View -viewId $seedId
Write-Host ("[Seed] Hiding wall elements in viewId={0}" -f $seedId) -ForegroundColor Cyan
$WALL = -2000011
$seedWallIds = Get-IdsInView -viewId $seedId -includeCatIds @($WALL) -excludeCatIds @()
Hide-Elements-Batched -viewId $seedId -ids $seedWallIds
$ren1 = Rename-View -viewId $seedId -newName 'Seed'
Write-Host ("[Seed] Ready viewId={0} name='Seed' (rename {1})" -f $seedId, ($ren1 ? 'ok' : 'skipped')) -ForegroundColor Green

# Type-specific views
Write-Host '[Types] Grouping walls by type in active view' -ForegroundColor Cyan
$groups = Get-WallTypeGroups -viewId $activeId
$keys = @($groups.Keys)
Write-Host ("[Types] Found {0} type group(s)" -f $keys.Count) -ForegroundColor Cyan
if($keys.Count -eq 0){ Write-Host 'Done. No wall types found.' -ForegroundColor Yellow; return }

$typeViews = @()
foreach($tn in $keys){
  $vid = $null
  if($ReuseExisting -and (Test-Path $MANIFEST)){
    try{
      $mf2 = Get-Content -LiteralPath $MANIFEST -Raw -Encoding UTF8 | ConvertFrom-Json -Depth 60
      foreach($tv in @($mf2.typeViews)){
        if(([string]$tv.name) -eq [string]$tn){ $vid = [int]$tv.viewId; break }
      }
    }catch{}
  }
  if(-not $vid){
    Write-Host ("[Type] Duplicating: {0}" -f $tn) -ForegroundColor Cyan
    $vid = Duplicate-View -viewId $activeId -prefix ''
  } else {
    Write-Host ("[Type] Reusing existing viewId={0} for '{1}'" -f $vid, $tn) -ForegroundColor Cyan
  }
  Reset-View -viewId $vid
  $allWallIds = @(); foreach($arr in $groups.Values){ $allWallIds += @($arr) }
  $null = Isolate-By-Type -viewId $vid -typeName $tn -allWallIds $allWallIds -groupsMap $groups
  $ren = Rename-View -viewId $vid -newName $tn
  Write-Host ("  -> viewId={0} name='{1}' (rename {2})" -f $vid, $tn, ($ren ? 'ok' : 'skipped')) -ForegroundColor Green
  $typeViews += @{ name=$tn; viewId=$vid }
}

# Write manifest
try{
  $manifestObj = @{ ts=(Get-Date).ToString('s'); port=$Port; seedId=$seedId; typeViews=$typeViews }
  ($manifestObj | ConvertTo-Json -Depth 10) | Out-File -FilePath $MANIFEST -Encoding utf8
}catch{}

Write-Host 'Done. Created/updated Seed and per-type views (names: Seed / type names).' -ForegroundColor Green
