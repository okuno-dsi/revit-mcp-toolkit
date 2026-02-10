# @feature: isolate single wall type safe | keywords: 壁, スペース, ビュー
param(
  [int]$Port = 5210,
  [int]$TypeId = 0,
  [int]$BatchSize = 800,
  [int]$MaxMillisPerTx = 3000,
  [int]$WaitSec = 360,
  [int]$JobTimeoutSec = 360,
  [switch]$Activate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8 = '1'
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

$PY = Join-Path $PSScriptRoot 'send_revit_command_durable.py'

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

function Get-ElementsInfo([int[]]$ids){
  $rows = @()
  if(-not $ids -or $ids.Count -eq 0){ return $rows }
  $chunk = 200
  for($i=0; $i -lt $ids.Count; $i+=$chunk){
    $hi = [Math]::Min($i+$chunk-1,$ids.Count-1)
    $batch = @($ids[$i..$hi])
    $info = Invoke-Mcp 'get_element_info' @{ elementIds=@($batch); rich=$true } 300 300 -Force
    foreach($p in 'result.result.elements','result.elements','elements'){
      try{ $cur=$info; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $rows += @($cur); break }catch{}
    }
  }
  return $rows
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

# --- Main ---
$active = Get-ActiveView
$activeId = [int]$active.viewId
Write-Host ("[Active] viewId={0} name='{1}'" -f $activeId, $active.name) -ForegroundColor Cyan

# Duplicate and reset working view
$prefix = ('OneType_'+(Get-Date -Format 'yyyyMMdd_HHmmss')+' ')
$viewId = Duplicate-View -viewId $activeId -prefix $prefix
Reset-View -viewId $viewId
if($Activate){ try { $act = @{ viewId=$viewId } | ConvertTo-Json -Compress; & python $PY --port $Port --command activate_view --params $act --wait-seconds 30 2>$null | Out-Null } catch {} }

Write-Host '[Prepare] Collecting wall IDs and grouping by typeId' -ForegroundColor Cyan
$WALL = -2000011
$wallIds = Get-IdsInView -viewId $viewId -includeCatIds @($WALL) -excludeCatIds @()
if($wallIds.Count -eq 0){ throw 'No wall elements found in view.' }
$rows = Get-ElementsInfo -ids $wallIds

# Decide target typeId
if($TypeId -le 0){
  $grp = @{}
  foreach($e in $rows){
    try{ $tid = [int]$e.typeId }catch{ $tid = $null }
    if(-not $tid){ continue }
    if(-not $grp.ContainsKey($tid)){ $grp[$tid] = 0 }
    $grp[$tid] += 1
  }
  if($grp.Keys.Count -eq 0){ throw 'Could not resolve any typeId from walls.' }
  $TypeId = ($grp.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1).Key
}
Write-Host ("[Target] typeId={0}" -f $TypeId) -ForegroundColor Cyan

# Compute nonwalls + other walls
$nonwalls = @()
try{
  $shape = @{ idsOnly = $true; page = @{ limit = 200000 } }
  $filter = @{ modelOnly=$true; excludeImports=$true; excludeCategoryIds=@($WALL) }
  $res = Invoke-Mcp 'get_elements_in_view' @{ viewId=$viewId; _shape=$shape; _filter=$filter } 300 300 -Force
  foreach($p in 'result.result.elementIds','result.elementIds','elementIds'){
    try{ $cur=$res; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $nonwalls = @($cur | ForEach-Object { [int]$_ }); break }catch{}
  }
}catch{}

$thisTypeIds = @()
foreach($e in $rows){ try{ if([int]$e.typeId -eq $TypeId){ $thisTypeIds += [int]$e.elementId } }catch{} }
$otherWalls = @($wallIds | Where-Object { $thisTypeIds -notcontains $_ })

Write-Host ("[Hide] nonwalls={0} otherWalls={1}" -f $nonwalls.Count, $otherWalls.Count) -ForegroundColor Yellow
Hide-Elements-Batched -viewId $viewId -ids $nonwalls
Hide-Elements-Batched -viewId $viewId -ids $otherWalls

Write-Host ("Done. Isolated typeId={0} in viewId={1}" -f $TypeId, $viewId) -ForegroundColor Green

