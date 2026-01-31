# @feature: view isolation and dwg auto | keywords: 壁, スペース, ビュー, DWG
param(
  [int]$Port = 5210,
  [string]$OutDir = "",
  [string]$DwgVersion = 'ACAD2018',
  [int]$BatchSize = 800,
  [int]$MaxMillisPerTx = 3000,
  [int]$WaitSec = 360,
  [int]$JobTimeoutSec = 360,
  [switch]$KeepAnnotations
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8 = '1'
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

$ROOT = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$WORKROOT = Join-Path $ROOT 'Codex\Work'
if([string]::IsNullOrWhiteSpace($OutDir)){
  if(!(Test-Path $WORKROOT)) { New-Item -ItemType Directory -Path $WORKROOT -Force | Out-Null }
  $ts = Get-Date -Format 'yyyyMMdd_HHmmss'
  $OutDir = Join-Path $WORKROOT ("DWG_"+$ts)
}
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$LogsDir = Join-Path $OutDir 'Logs'
New-Item -ItemType Directory -Path $LogsDir -Force | Out-Null

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

function Select-BaseView {
  # Try open views, choose one with the most walls; fall back to current view
  $open = @()
  try {
    $lov = Invoke-Mcp 'list_open_views' @{} 120 120 -Force
    $open = @(Get-JsonPath $lov @('result.result.views','result.views','views'))
  } catch {}
  if(-not $open -or $open.Count -eq 0){
    $cv = Invoke-Mcp 'get_current_view' @{} 60 120 -Force
    $vid = 0
    foreach($p in 'result.result.viewId','result.viewId','viewId'){ try{ $cur=$cv; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $vid=[int]$cur; break }catch{} }
    if($vid -le 0){ throw 'Could not resolve a base view (no open views, get_current_view failed).' }
    return @{ viewId=$vid; name='(active)'; source='active' }
  }

  $WALL = -2000011
  $best = $null
  $bestC = -1
  foreach($v in $open){
    $vId = 0; $vName = ''
    try{ $vId = [int]$v.viewId }catch{}
    try{ $vName = [string]$v.name }catch{}
    if($vId -le 0){ continue }
    $shape = @{ idsOnly=$true; page=@{ limit=100000 } }
    $filter = @{ includeCategoryIds=@($WALL) }
    $res = Invoke-Mcp 'get_elements_in_view' @{ viewId=$vId; _shape=$shape; _filter=$filter } 240 240 -Force
    $ids = @()
    foreach($path in 'result.result.elementIds','result.elementIds','elementIds'){
      try{ $cur=$res; foreach($seg in $path.Split('.')){ $cur = $cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $ids=@($cur | ForEach-Object { [int]$_ }); break }catch{}
    }
    $cnt = ($ids|Measure-Object).Count
    if($cnt -gt $bestC){ $best = @{ viewId=$vId; name=$vName; count=$cnt; source='open' }; $bestC=$cnt }
  }
  if(-not $best){ throw 'No suitable base view found in open views.' }
  return $best
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

function Duplicate-View([int]$viewId,[string]$prefix){
  $dup = Invoke-Mcp 'duplicate_view' @{ viewId=$viewId; withDetailing=$true; namePrefix=$prefix; __smoke_ok=$true } 120 240 -Force
  foreach($p in 'result.result.viewId','result.viewId','viewId'){
    try{ $cur=$dup; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; return [int]$cur }catch{}
  }
  throw 'duplicate_view did not return viewId'
}

function Export-Dwg([int]$viewId,[string]$fileName){
  $outAbs = (Resolve-Path $OutDir).Path.Replace('\\','/')
  $params = @{ viewId=$viewId; outputFolder=$outAbs; fileName=$fileName; dwgVersion=$DwgVersion; __smoke_ok=$true }
  $res = Invoke-Mcp 'export_dwg' $params 600 600 -Force
  try { $res | ConvertTo-Json -Depth 40 | Out-File -FilePath (Join-Path $LogsDir ("export_"+$fileName+".json")) -Encoding utf8 } catch {}
  return $res
}

function Sanitize-Stem([string]$s){ if([string]::IsNullOrWhiteSpace($s)){ return 'UNKNOWN' }; return ($s -replace '[^A-Za-z0-9_-]','_') }

function Get-WallTypeGroups([int]$viewId){
  $WALL = -2000011
  $wallIds = Get-IdsInView -viewId $viewId -includeCatIds @($WALL) -excludeCatIds @()
  $groups = @{}
  if($wallIds.Count -eq 0){ return $groups }
  # Fetch element info in chunks and group by typeName
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
      # Try multiple paths to get the wall type name
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
        try{ $tn = [string]$e.parameters.'�^�C�v��'.value }catch{}
      }
      if([string]::IsNullOrWhiteSpace($tn)){ $tn = 'WT' }
      $stem = Sanitize-Stem $tn
      if(-not $groups.ContainsKey($stem)){ $groups[$stem] = @{ typeName=$tn; elementIds=(New-Object System.Collections.Generic.List[int]) } }
      [void]$groups[$stem].elementIds.Add($eid)
    }
  }
  return $groups
}

function Isolate-By-Type([int]$viewId,[string]$typeName){
  $keepAnn = $true; if($PSBoundParameters.ContainsKey('KeepAnnotations')){ $keepAnn = [bool]$KeepAnnotations }
  $filter = @{ includeClasses=@('Wall'); parameterRules=@(@{ target='type'; builtInName='SYMBOL_NAME_PARAM'; op='eq'; value=$typeName }) }
  $params = @{ viewId=$viewId; detachViewTemplate=$true; keepAnnotations=$keepAnn; filter=$filter; __smoke_ok=$true }
  return (Invoke-Mcp 'isolate_by_filter_in_view' $params 300 300 -Force)
}

#
# Main flow: base -> seed (no walls) -> per-wall-type views -> DWG exports
#

Write-Host '[Base] Selecting base view automatically (no hardcoded names/ids)' -ForegroundColor Cyan
$base = Select-BaseView
$baseViewId = [int]$base.viewId
$baseName = try{ [string]$base.name } catch { '(base)' }
Write-Host ("[Base] viewId={0} name='{1}' source={2}" -f $baseViewId, $baseName, $base.source) -ForegroundColor Cyan

# Seed view
$seedPrefix = ('AutoSeed_'+(Get-Date -Format 'yyyyMMdd_HHmmss')+' ')
Write-Host ("[Seed] Duplicating base -> prefix '{0}'" -f $seedPrefix) -ForegroundColor Cyan
$seedId = Duplicate-View -viewId $baseViewId -prefix $seedPrefix
Write-Host ("[Seed] Resetting viewId={0} (template detach + unhide + clear overrides)" -f $seedId) -ForegroundColor Cyan
Reset-View -viewId $seedId
Write-Host ("[Seed] Hiding all wall elements in viewId={0}" -f $seedId) -ForegroundColor Cyan
$WALL = -2000011
$seedWallIds = Get-IdsInView -viewId $seedId -includeCatIds @($WALL) -excludeCatIds @()
Hide-Elements-Batched -viewId $seedId -ids $seedWallIds
# Validate: expect 0 walls
$chk = Get-IdsInView -viewId $seedId -includeCatIds @($WALL) -excludeCatIds @()
Write-Host ("[Seed] Wall count after hide: {0}" -f $chk.Count)

# Export DWG for seed
Write-Host '[Seed] Exporting DWG (non-walls only remain visible in seed duplicate)' -ForegroundColor Green
$seedRes = Export-Dwg -viewId $seedId -fileName 'seed'
try { if(($seedRes.ok -ne $true) -and ($seedRes.result.result.ok -ne $true)){ Write-Warning ("Seed export may have failed: " + ($seedRes | ConvertTo-Json -Depth 10)) } } catch {}

# Type-specific views
Write-Host '[Types] Grouping walls by type in base view' -ForegroundColor Cyan
$groups = Get-WallTypeGroups -viewId $baseViewId
$typeKeys = @($groups.Keys)
Write-Host ("[Types] Found {0} wall type group(s)" -f $typeKeys.Count) -ForegroundColor Cyan
foreach($key in $typeKeys){
  $tn = [string]$groups[$key].typeName
  $dupPrefix = ('AutoType_'+(Sanitize-Stem $tn)+'_'+(Get-Date -Format 'yyyyMMdd_HHmmss')+' ')
  Write-Host ("[Type] '{0}' -> duplicating base view with prefix '{1}'" -f $tn, $dupPrefix) -ForegroundColor Cyan
  $vid = Duplicate-View -viewId $baseViewId -prefix $dupPrefix
  Write-Host ("[Type] Isolating type in viewId={0}" -f $vid) -ForegroundColor Cyan
  $iso = Isolate-By-Type -viewId $vid -typeName $tn
  $fname = ('walls_'+(Sanitize-Stem $tn))
  Write-Host ("[Type] Exporting DWG '{0}.dwg'" -f $fname) -ForegroundColor Green
  $tres = Export-Dwg -viewId $vid -fileName $fname
  try { if(($tres.ok -ne $true) -and ($tres.result.result.ok -ne $true)){ Write-Warning ("Type export may have failed: " + ($tres | ConvertTo-Json -Depth 10)) } } catch {}
}

Write-Host 'Completed: Seed + per-type DWG exports (no hardcoded names/ids).' -ForegroundColor Green
