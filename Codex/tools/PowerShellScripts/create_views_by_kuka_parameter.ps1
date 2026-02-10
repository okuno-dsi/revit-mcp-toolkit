# @feature: create views by kuka parameter | keywords: 壁, スペース, ビュー, レベル
param(
  [int]$Port = 5210,
  [int]$BatchSize = 800,
  [int]$MaxMillisPerTx = 3000,
  [int]$WaitSec = 360,
  [int]$JobTimeoutSec = 360,
  [switch]$Activate,
  [switch]$ReuseExisting,
  [int]$SampleWalls = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8 = '1'
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

$PY = Join-Path $PSScriptRoot 'send_revit_command_durable.py'

function Resolve-LogsDir([int]$p){
  $workRoot = Resolve-Path (Join-Path $PSScriptRoot '..\\..\\..\\Projects')
  $cands = Get-ChildItem -LiteralPath $workRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*_$p" }
  $chosen = $null
  if($cands){ $chosen = ($cands | Where-Object { $_.Name -notlike 'Project_*' } | Select-Object -First 1); if(-not $chosen){ $chosen = $cands | Select-Object -First 1 } }
  if(-not $chosen){ $chosen = New-Item -ItemType Directory -Path (Join-Path $workRoot ("Project_{0}" -f $p)) }
  $logs = Join-Path $chosen.FullName 'Logs'
  if(-not (Test-Path $logs)){ New-Item -ItemType Directory -Path $logs | Out-Null }
  return $logs
}

$LOGS = Resolve-LogsDir -p $Port
$MANIFEST = Join-Path $LOGS 'create_views_by_kuka_parameter.manifest.json'

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

function Find-KukaParamNames([int[]]$sampleWallIds){
  $names = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
  foreach($wid in $sampleWallIds){
    try{
      $res = Invoke-Mcp 'list_wall_parameters' @{ elementId = $wid } 120 240 -Force
      $rows = Get-JsonPath $res @('result.result.parameters','result.parameters','parameters')
      foreach($p in @($rows)){
        try{
          $nm = [string]$p.name
          $dt = ''
          try{ $dt = [string]$p.dataType }catch{}
          if($nm -and $nm -like '*区画*'){
            # Prefer boolean-ish
            if(($dt -and $dt -like '*bool*') -or $p.storageType -eq 'Integer' -or $p.storageType -eq 'Double' -or $p.storageType -eq 'String'){
              [void]$names.Add($nm)
            }
          }
        }catch{}
      }
    }catch{}
  }
  return @($names)
}

function Get-KukaGroups([int[]]$allWallIds,[string[]]$paramNames){
  $groups = @{}
  foreach($pn in $paramNames){ $groups[$pn] = New-Object System.Collections.Generic.List[int] }
  if($allWallIds.Count -eq 0 -or $paramNames.Count -eq 0){ return $groups }
  $paramKeys = @(); foreach($pn in $paramNames){ $paramKeys += @{ name = $pn } }
  $chunk = 200
  for($i=0; $i -lt $allWallIds.Count; $i+=$chunk){
    $hi = [Math]::Min($i+$chunk-1,$allWallIds.Count-1)
    $batch = @($allWallIds[$i..$hi])
    $params = @{ elementIds = @($batch); paramKeys = @($paramKeys); page = @{ startIndex = 0; batchSize = 500 } }
    $res = Invoke-Mcp 'get_instance_parameters_bulk' $params 300 300 -Force
    $items = Get-JsonPath $res @('result.result.items','result.items','items')
    foreach($it in @($items)){
      $eid = 0
      try{ $eid = [int]$it.elementId }catch{}
      if($eid -le 0){ continue }
      foreach($pn in $paramNames){
        try{
          $v = $null
          if($it.params -and $it.params.PSObject.Properties[$pn]){ $v = $it.params.$pn }
          $disp = $null
          if($it.display -and $it.display.PSObject -and $it.display.PSObject.Properties[$pn]){ $disp = [string]$it.display.$pn }
          $on = $false
          if($v -is [bool]){ $on = [bool]$v }
          elseif($v -is [int]){ $on = ([int]$v -ne 0) }
          elseif($v -is [double]){ $on = ([double]$v -ne 0) }
          elseif($v){ $on = ([string]$v).Trim().ToLowerInvariant() -in @('true','はい','yes','on') }
          if(-not $on -and $disp){
            $on = ($disp.Trim().ToLowerInvariant() -in @('true','はい','yes','on','checked'))
          }
          if($on){ [void]$groups[$pn].Add($eid) }
        }catch{}
      }
    }
  }
  return $groups
}

function Rename-View([int]$viewId,[string]$newName){
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

function Delete-Views-From-Manifest(){
  if(!(Test-Path $MANIFEST)){ return }
  try{
    $mf = Get-Content -LiteralPath $MANIFEST -Raw -Encoding UTF8 | ConvertFrom-Json -Depth 60
    $ids = @()
    if($mf.seedId){ $ids += [int]$mf.seedId }
    foreach($v in @($mf.kukaViews)){
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

# Determine base view for duplication (if active is non-graphical, create a plan view on first level)
$baseViewId = $activeId
try {
  $null = Duplicate-View -viewId $activeId -prefix '' | Out-Null
} catch {
  Write-Host '[Base] Active view not duplicable (non-graphical?). Creating a Plan view from first Level.' -ForegroundColor Yellow
  # Get first level id
  $lv = Invoke-Mcp 'get_levels' @{ _shape = @{ idsOnly = $false } } 120 240 -Force
  $levels = Get-JsonPath $lv @('result.result.levels','result.levels','levels','result.result.rows')
  $levelId = 0
  foreach($L in @($levels)){
    try{ $levelId = [int]$L.elementId }catch{}
    if($levelId -gt 0){ break }
  }
  if($levelId -le 0){ throw 'Could not resolve any Level for plan view.' }
  $cv = Invoke-Mcp 'create_view_plan' @{ levelId=$levelId; __smoke_ok=$true } 120 240 -Force
  $newVid = 0
  foreach($p in 'result.result.viewId','result.viewId','viewId'){
    try{ $cur=$cv; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $newVid=[int]$cur; break }catch{}
  }
  if($newVid -le 0){ throw 'create_view_plan did not return viewId' }
  $baseViewId = $newVid
  Write-Host ("[Base] Using new Plan viewId={0} as base" -f $baseViewId) -ForegroundColor Cyan
}

if(-not $ReuseExisting){ Delete-Views-From-Manifest }

# Seed view (no walls)
Write-Host "[Seed] Preparing SEED view" -ForegroundColor Cyan
$seedId = $null
if($ReuseExisting -and (Test-Path $MANIFEST)){
  try{ $mf = Get-Content -LiteralPath $MANIFEST -Raw -Encoding UTF8 | ConvertFrom-Json -Depth 60; $seedId = [int]$mf.seedId }catch{}
}
if(-not $seedId){
  Write-Host ("[Seed] Duplicating base viewId={0}" -f $baseViewId) -ForegroundColor Cyan
  $seedId = Duplicate-View -viewId $baseViewId -prefix ''
}
if($Activate){ try { $act = @{ viewId=$seedId } | ConvertTo-Json -Compress; & python $PY --port $Port --command activate_view --params $act --wait-seconds 30 2>$null | Out-Null } catch {} }
Write-Host ("[Seed] Resetting viewId={0}" -f $seedId) -ForegroundColor Cyan
Reset-View -viewId $seedId
Write-Host ("[Seed] Hiding wall elements in viewId={0}" -f $seedId) -ForegroundColor Cyan
$WALL = -2000011
$seedWallIds = Get-IdsInView -viewId $seedId -includeCatIds @($WALL) -excludeCatIds @()
Hide-Elements-Batched -viewId $seedId -ids $seedWallIds
$renSeed = Rename-View -viewId $seedId -newName 'SEED'
Write-Host ("[Seed] Ready viewId={0} name='SEED' (rename {1})" -f $seedId, ($renSeed ? 'ok' : 'skipped')) -ForegroundColor Green

# Discover walls in base view
Write-Host ("[Walls] Collecting wall IDs in base viewId={0}" -f $baseViewId) -ForegroundColor Cyan
$allWallIds = Get-IdsInView -viewId $baseViewId -includeCatIds @($WALL) -excludeCatIds @()
if($allWallIds.Count -eq 0){ Write-Host 'No walls in active view. Done.' -ForegroundColor Yellow; return }

# Discover 区画 parameter names from samples
Write-Host ("[Params] Discovering '区画' parameter names from up to {0} sample wall(s)" -f [Math]::Min($SampleWalls,$allWallIds.Count)) -ForegroundColor Cyan
$take = [Math]::Min($SampleWalls, $allWallIds.Count)
$sampleIds = @($allWallIds[0..($take-1)])
$kukaNames = Find-KukaParamNames -sampleWallIds $sampleIds
if($kukaNames.Count -eq 0){ Write-Host "No '区画' parameters found on sample walls. Nothing to do." -ForegroundColor Yellow; return }
Write-Host ("[Params] Found names: {0}" -f ([string]::Join(', ', $kukaNames))) -ForegroundColor DarkCyan

# Group by 区画 parameter (on only)
Write-Host '[Group] Building groups per 区画 parameter (value == on/はい/true)' -ForegroundColor Cyan
$groups = Get-KukaGroups -allWallIds $allWallIds -paramNames $kukaNames

# Create views per 区画 param
$kukaViews = @()
foreach($pn in $kukaNames){
  $target = @($groups[$pn])
  if(-not $target -or $target.Count -eq 0){ Write-Host ("[Skip] No walls with '{0}'==on" -f $pn) -ForegroundColor DarkGray; continue }

  $vid = $null
  if($ReuseExisting -and (Test-Path $MANIFEST)){
    try{
      $mf2 = Get-Content -LiteralPath $MANIFEST -Raw -Encoding UTF8 | ConvertFrom-Json -Depth 60
      foreach($tv in @($mf2.kukaViews)){
        if(([string]$tv.name) -eq [string]$pn){ $vid = [int]$tv.viewId; break }
      }
    }catch{}
  }
  if(-not $vid){
    Write-Host ("[View] Duplicating for '{0}' from base viewId={1}" -f $pn, $baseViewId) -ForegroundColor Cyan
    $vid = Duplicate-View -viewId $baseViewId -prefix ''
  } else {
    Write-Host ("[View] Reusing existing viewId={0} for '{1}'" -f $vid, $pn) -ForegroundColor Cyan
  }

  Reset-View -viewId $vid
  # Hide non-walls + walls not in target
  $nonwalls = Get-IdsInView -viewId $vid -includeCatIds @() -excludeCatIds @($WALL)
  $otherWalls = @($allWallIds | Where-Object { $target -notcontains $_ })
  $hideIds = @(); if($nonwalls.Count){ $hideIds += $nonwalls }; if($otherWalls.Count){ $hideIds += $otherWalls }
  Hide-Elements-Batched -viewId $vid -ids $hideIds
  $ren = Rename-View -viewId $vid -newName $pn
  Write-Host ("  -> viewId={0} name='{1}' (rename {2})" -f $vid, $pn, ($ren ? 'ok' : 'skipped')) -ForegroundColor Green
  $kukaViews += @{ name=$pn; viewId=$vid; count=$target.Count }
}

try{
  $manifestObj = @{ ts=(Get-Date).ToString('s'); port=$Port; seedId=$seedId; kukaViews=$kukaViews; paramNames=$kukaNames }
  ($manifestObj | ConvertTo-Json -Depth 10) | Out-File -FilePath $MANIFEST -Encoding utf8
}catch{}

Write-Host 'Done. Created/updated SEED and per-区画 views.' -ForegroundColor Green


