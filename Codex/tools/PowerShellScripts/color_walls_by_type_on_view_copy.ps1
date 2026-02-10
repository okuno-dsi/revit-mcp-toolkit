# @feature: color walls by type on view copy | keywords: 壁, スペース, ビュー
param(
  [int]$Port = 5210,
  [int]$Transparency = 25,
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
  do {
    $params = @{ viewId=$viewId; detachViewTemplate=$true; includeTempReset=$true; unhideElements=$true; clearElementOverrides=$true; batchSize=$BatchSize; startIndex=$idx; refreshView=$true; __smoke_ok=$true }
    $r = Invoke-Mcp 'show_all_in_view' $params 300 300 -Force
    $nxt = $null
    foreach($p in 'result.result.nextIndex','result.nextIndex','nextIndex'){ try{ $cur=$r; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $nxt=$cur; break }catch{} }
    if($null -ne $nxt){ try{ $idx = [int]$nxt }catch{ $idx = 0 } } else { $idx = 0 }
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
        try{ $tn = [string]$e.parameters.'�^�C�v��'.value }catch{}
      }
      if([string]::IsNullOrWhiteSpace($tn)){ $tn = 'WT' }
      if(-not $groups.ContainsKey($tn)){ $groups[$tn] = New-Object System.Collections.Generic.List[int] }
      [void]$groups[$tn].Add($eid)
    }
  }
  return $groups
}

function Get-Palette(){
  # Distinct, bright-ish palette
  return @(
    @{r=230;g=25;b=75},    @{r=60;g=180;b=75},   @{r=0;g=130;b=200},
    @{r=245;g=130;b=48},   @{r=145;g=30;b=180},  @{r=70;g=240;b=240},
    @{r=240;g=50;b=230},   @{r=210;g=245;b=60},  @{r=250;g=190;b=190},
    @{r=0;g=128;b=128},    @{r=128;g=0;b=0},     @{r=0;g=0;b=128},
    @{r=128;g=128;b=0},    @{r=255;g=215;b=0},   @{r=0;g=191;b=255},
    @{r=255;g=105;b=180},  @{r=154;g=205;b=50},  @{r=255;g=140;b=0}
  )
}

function Apply-Color-To-Ids([int]$viewId,[int[]]$ids,[int]$r,[int]$g,[int]$b){
  if(-not $ids -or $ids.Count -eq 0){ return }
  $start = 0
  while($true){
    $params = @{ viewId=$viewId; elementIds=@($ids); r=$r; g=$g; b=$b; transparency=$Transparency; detachViewTemplate=$true; batchSize=$BatchSize; maxMillisPerTx=$MaxMillisPerTx; startIndex=$start; refreshView=$true; __smoke_ok=$true }
    $res = Invoke-Mcp 'set_visual_override' $params 300 300 -Force
    $completed = $false
    foreach($p in 'result.result.completed','result.completed','completed'){ try{ $cur=$res; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $completed = [bool]$cur; break }catch{} }
    $next = $null
    foreach($p in 'result.result.nextIndex','result.nextIndex','nextIndex'){ try{ $cur=$res; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $next = $cur; break }catch{} }
    if(-not $completed -and $next -ne $null){ try{ $start = [int]$next }catch{ $start = 0 } } else { break }
  }
}

# 1) Resolve active view and duplicate it (with detailing), detach template, clear overrides
$active = Get-ActiveView
$activeId = [int]$active.viewId
Write-Host ("[Active] viewId={0} name='{1}'" -f $activeId, $active.name) -ForegroundColor Cyan

$prefix = ('ColorByType_'+(Get-Date -Format 'yyyyMMdd_HHmmss')+' ')
Write-Host ("[Duplicate] prefix='{0}'" -f $prefix) -ForegroundColor Cyan
$viewId = Duplicate-View -viewId $activeId -prefix $prefix

if($Activate){
  try { $act = @{ viewId=$viewId } | ConvertTo-Json -Compress; & python $PY --port $Port --command activate_view --params $act --wait-seconds 30 2>$null | Out-Null } catch {}
}

Write-Host ("[Reset] viewId={0}: detach template + unhide + clear overrides" -f $viewId) -ForegroundColor Cyan
Reset-View -viewId $viewId

# 2) Group walls by type in the duplicated view
Write-Host '[Group] Collecting walls and grouping by type...' -ForegroundColor Cyan
$groups = Get-WallTypeGroups -viewId $viewId
if($groups.Keys.Count -eq 0){ Write-Warning 'No walls found in duplicated view. Nothing to color.'; return }

$palette = Get-Palette
$i = 0
Write-Host ("[Color] Assigning colors to {0} type group(s)" -f $groups.Keys.Count) -ForegroundColor Cyan
foreach($tn in (@($groups.Keys) | Sort-Object)){
  $col = $palette[$i % $palette.Count]
  $ids = @($groups[$tn] | ForEach-Object { [int]$_ })
  Write-Host ("  - {0} : RGB({1},{2},{3}) elements={4}" -f $tn, $col.r, $col.g, $col.b, $ids.Count) -ForegroundColor DarkGray
  Apply-Color-To-Ids -viewId $viewId -ids $ids -r $col.r -g $col.g -b $col.b
  $i++
}

Write-Host ("Done. View colored by wall type. New viewId={0}" -f $viewId) -ForegroundColor Green

