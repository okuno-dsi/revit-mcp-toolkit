# @feature: export walls by type | keywords: 壁, スペース, ビュー, タグ, DWG, レベル
param(
  [int]$Port = 5210,
  [string]$OutDir = "Work/AutoCadOut",
  [switch]$AutoMerge,
  [switch]$Clean,
  [string]$AccorePath = "C:/Program Files/Autodesk/AutoCAD 2025/accoreconsole.exe",
  [string]$Locale = "en-US",
  [int]$AccoreTimeoutMs = 600000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8 = '1'

$SCRIPT_DIR = $PSScriptRoot
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'
$LOGS = Resolve-Path (Join-Path $SCRIPT_DIR '..\Logs')

function Invoke-McpDurable {
  param(
    [int]$Port,
    [string]$Method,
    [hashtable]$Params,
    [double]$WaitSec = 120,
    [switch]$Force
  )
  $pjson = ($Params | ConvertTo-Json -Depth 50 -Compress)
  $args = @('--port', $Port, '--command', $Method, '--params', $pjson, '--wait-seconds', [string]$WaitSec)
  if($Force){ $args += '--force' }
  $raw = & python $PY @args 2>$null
  if($LASTEXITCODE -ne 0){
    throw "MCP call failed ($Method): $raw"
  }
  try { return $raw | ConvertFrom-Json -Depth 100 } catch { throw "Invalid JSON from MCP ($Method): $raw" }
}

function Get-ActiveViewId {
  param([int]$Port)
  $bootPath = Join-Path $LOGS 'agent_bootstrap.json'
  if(!(Test-Path $bootPath)){
    Write-Host '[Bootstrap] ping + agent_bootstrap' -ForegroundColor Cyan
    Invoke-McpDurable -Port $Port -Method 'ping_server' -Params @{}
    $res = Invoke-McpDurable -Port $Port -Method 'agent_bootstrap' -Params @{}
    $res | ConvertTo-Json -Depth 50 | Out-File -FilePath $bootPath -Encoding utf8
  }
  $boot = Get-Content $bootPath -Raw | ConvertFrom-Json -Depth 100
  try { return [int]$boot.result.result.environment.activeViewId } catch { throw 'activeViewId not found in agent_bootstrap.json' }
}

function Get-ElementsInViewIds {
  param([int]$Port, [int]$ViewId)
  $shape = @{ idsOnly = $true; page = @{ limit = 20000 } }
  $res = Invoke-McpDurable -Port $Port -Method 'get_elements_in_view' -Params @{ viewId=$ViewId; _shape=$shape }
  # tolerate envelopes
  $rows = @()
  foreach($path in 'result.result.elementIds','result.result.rows','result.rows','result.elementIds','elementIds','rows'){
    try{
      $cur = $res
      foreach($seg in $path.Split('.')){ $cur = $cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }
      if($cur){ $rows = $cur; break }
    }catch{}
  }
  if(-not $rows){ throw 'Could not read rows from get_elements_in_view response' }
  return @($rows | ForEach-Object { [int]$_ })
}

function Get-ElementsInViewRows {
  param([int]$Port, [int]$ViewId)
  $shape = @{ idsOnly = $false; page = @{ limit = 20000 } }
  $res = Invoke-McpDurable -Port $Port -Method 'get_elements_in_view' -Params @{ viewId=$ViewId; _shape=$shape }
  $rows = @()
  foreach($path in 'result.result.rows','result.rows','rows'){
    try{
      $cur = $res
      foreach($seg in $path.Split('.')){ $cur = $cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }
      if($cur){ $rows = $cur; break }
    }catch{}
  }
  if(-not $rows){ throw 'Could not read rows from get_elements_in_view (rows) response' }
  return @($rows)
}

function Try-CreateExportView {
  param([int]$Port, [int]$ActiveViewId)
  # Derive a levelName by majority from current view rows
  $rows = @()
  try { $rows = Get-ElementsInViewRows -Port $Port -ViewId $ActiveViewId } catch {}
  $lvl = $null
  if($rows.Count -gt 0){
    $groups = $rows | Where-Object { $_.levelName } | Group-Object -Property levelName | Sort-Object Count -Descending
    if($groups.Count -gt 0){ $lvl = [string]$groups[0].Name }
  }
  if([string]::IsNullOrWhiteSpace($lvl)){ $lvl = '2FL' }
  $vname = "Codex_Export_" + (Get-Date -Format 'yyyyMMdd_HHmmss')
  $payload = @{ levelName = $lvl; name = $vname; __smoke_ok = $true } | ConvertTo-Json -Compress
  $raw = & python $PY --port $Port --command create_view_plan --params $payload --wait-seconds 90 2>$null
  if($LASTEXITCODE -ne 0){ return $null }
  try { $obj = $raw | ConvertFrom-Json -Depth 40 } catch { return $null }
  $newId = $null
  try { $newId = [int]$obj.result.result.viewId } catch {}
  if(-not $newId){ try { $newId = [int]$obj.result.viewId } catch {} }
  if(-not $newId){ return $null }
  # Activate view (per Quickstart)
  $act = @{ viewId = $newId } | ConvertTo-Json -Compress
  & python $PY --port $Port --command activate_view --params $act --wait-seconds 30 2>$null | Out-Null
  # Clear view template on the new view
  $clear = @{ viewId = $newId; clear = $true; __smoke_ok = $true } | ConvertTo-Json -Compress
  & python $PY --port $Port --command set_view_template --params $clear --wait-seconds 60 2>$null | Out-Null
  # Optional: fit
  $fit = @{ viewId = $newId } | ConvertTo-Json -Compress
  & python $PY --port $Port --command view_fit --params $fit --wait-seconds 30 2>$null | Out-Null
  return $newId
}

function Chunk([object[]]$arr, [int]$size){
  for($i=0; $i -lt $arr.Count; $i+=$size){ $len = [Math]::Min($size, $arr.Count-$i); $arr[$i..($i+$len-1)] }
}

function Get-ElementInfoBulk {
  param([int]$Port, [int[]]$Ids)
  $elements = New-Object System.Collections.Generic.List[object]
  foreach($batch in (Chunk $Ids 50)){
    $res = Invoke-McpDurable -Port $Port -Method 'get_element_info' -Params @{ elementIds = $batch; rich = $true } -WaitSec 180
    $found = $null
    try { $found = $res.result.result.elements } catch {}
    if(-not $found){ try { $found = $res.result.elements } catch {} }
    if(-not $found){ try { $found = $res.elements } catch {} }
    if($found){
      if($found -is [System.Array]){ $elements.AddRange([object[]]$found) }
      else { $elements.Add($found) }
    }
  }
  return $elements.ToArray()
}

function Get-PropOr($obj, [string[]]$paths, $default){
  foreach($p in $paths){
    try{
      $cur = $obj
      foreach($seg in $p.Split('.')){ $cur = $cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }
      if($null -ne $cur -and $cur.ToString().Length -gt 0){ return $cur }
    }catch{}
  }
  return $default
}

function Sanitize-Stem([string]$s){
  if([string]::IsNullOrWhiteSpace($s)){ return 'WT' }
  $x = $s -replace '[^A-Za-z0-9_\-]','_'
  if($x.Length -gt 48){ $x = $x.Substring(0,48) }
  return $x
}

# --- main flow ---
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $OutDir 'Merged') | Out-Null
$OUTABS = (Resolve-Path $OutDir).Path

if($Clean){
  Write-Host "[Clean] Removing old DWGs in $OutDir" -ForegroundColor Yellow
  Get-ChildItem -Force -Path $OutDir -Filter '*.dwg' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
  $mrg = Join-Path $OutDir 'Merged'
  if(Test-Path $mrg){ Get-ChildItem -Force -Path $mrg -Filter '*.dwg' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue }
  $cmdFile = Join-Path $OutDir 'command.txt'
  if(Test-Path $cmdFile){ Remove-Item -Force -ErrorAction SilentlyContinue $cmdFile }
}

$activeViewId = Get-ActiveViewId -Port $Port
Write-Host "[View] activeViewId=$activeViewId" -ForegroundColor Cyan
Write-Host "[View] creating export view (template-free)" -ForegroundColor Cyan
$viewId = Try-CreateExportView -Port $Port -ActiveViewId $activeViewId
if(-not $viewId){
  Write-Warning "Failed to create export view; falling back to active view and attempting to clear template"
  $vt = @{ viewId = $activeViewId; clear = $true; __smoke_ok = $true } | ConvertTo-Json -Compress
  & python $PY --port $Port --command set_view_template --params $vt --wait-seconds 60 2>$null | Out-Null
  $viewId = $activeViewId
}
Write-Host "[View] using viewId=$viewId" -ForegroundColor Cyan

# List all elements in view and fetch info
$allIds = Get-ElementsInViewIds -Port $Port -ViewId $viewId
Write-Host ("[Elements] in view: {0}" -f $allIds.Count)
if($allIds.Count -eq 0){ throw 'No elements found in current view.' }

$rows = Get-ElementsInViewRows -Port $Port -ViewId $viewId
$WALL_CAT_ID = -2000011
$rowWalls = @($rows | Where-Object { ([int]($_.categoryId)) -eq $WALL_CAT_ID })
$rowNonWalls = @($rows | Where-Object { ([int]($_.categoryId)) -ne $WALL_CAT_ID })
if($rowWalls.Count -eq 0){ throw 'No walls found in current view.' }

function RowsToIds($rows){ @($rows | ForEach-Object { try { [int]$_.elementId } catch { 0 } } | Where-Object { $_ -gt 0 }) }
$wallIds = RowsToIds $rowWalls
$nonwallIds = RowsToIds $rowNonWalls

# Fetch type per wall id (robust and cached)
$groups = @{}
$typeCache = @{}
$i = 0
foreach($wid in $wallIds){
  $i++
  if(($i % 25) -eq 0){ Write-Host ("[WallInfo] progress {0}/{1}" -f $i, $wallIds.Count) }
  $payload = @{ elementIds = @($wid); rich = $true } | ConvertTo-Json -Compress
  $raw = & python $PY --port $Port --command get_element_info --params $payload --wait-seconds 60 2>$null
  if($LASTEXITCODE -ne 0){ continue }
  try { $obj = $raw | ConvertFrom-Json -Depth 50 } catch { continue }
  $elem = $null
  try { $elem = $obj.result.result.elements[0] } catch {}
  if(-not $elem){ try { $elem = $obj.result.elements[0] } catch {} }
  if(-not $elem){ try { $elem = $obj.elements[0] } catch {} }
  if(-not $elem){ continue }
  $tid = $null
  try { $tid = [int]$elem.typeId } catch {}
  $tn = $null
  if($tid -and $typeCache.ContainsKey($tid)){ $tn = $typeCache[$tid] }
  if(-not $tn){
    $tn = [string](Get-PropOr $elem @('typeName','symbol.name','type.name','parameters.Type Name.value','parameters.タイプ名.value') '')
    if([string]::IsNullOrWhiteSpace($tn)){ $tn = 'WT' }
    if($tid){ $typeCache[$tid] = $tn }
  }
  $stem = Sanitize-Stem $tn
  if(-not $groups.ContainsKey($stem)){ $groups[$stem] = New-Object System.Collections.ArrayList }
  [void]$groups[$stem].Add([int]$wid)
}

# Helper to extract numeric ids
function To-Ids($arr){ @($arr | ForEach-Object { try { [int]$_ } catch { 0 } } | Where-Object { $_ -gt 0 }) }

Write-Host ("[Walls] total={0} groups={1}" -f $wallIds.Count, $groups.Keys.Count)

# 1) Export seed.dwg (non-walls only): hide wall category, export, restore
Write-Host '[Seed] hide wall category and export seed.dwg' -ForegroundColor Cyan
$catHide = @{ viewId=$viewId; categoryIds=@(-2000011); visible=$false; __smoke_ok=$true } | ConvertTo-Json -Compress
& python $PY --port $Port --command set_category_visibility --params $catHide --wait-seconds 60 2>$null | Out-Null
$seedParams = @{ viewId=$viewId; outputFolder=$OUTABS.Replace('\\','/'); fileName='seed'; dwgVersion='ACAD2018'; __smoke_ok=$true }
$seedRes = Invoke-McpDurable -Port $Port -Method 'export_dwg' -Params $seedParams -WaitSec 300 -Force
$catShow = @{ viewId=$viewId; categoryIds=@(-2000011); visible=$true; __smoke_ok=$true } | ConvertTo-Json -Compress
& python $PY --port $Port --command set_category_visibility --params $catShow --wait-seconds 60 2>$null | Out-Null
try { $seedRes | ConvertTo-Json -Depth 20 | Out-File -FilePath (Join-Path $LOGS 'export_seed.json') -Encoding utf8 } catch {}
try { if($seedRes.result.result.ok -ne $true){ Write-Warning ("Seed export failed: " + ($seedRes | ConvertTo-Json -Depth 10)) } } catch {}

# 2) For each wall type group: hide non-walls + other walls, export walls_<stem>.dwg, reset
$wallDwgs = @()
foreach($stem in $groups.Keys){
  $arr = $groups[$stem]
  $idsThis = To-Ids $arr
  $idsOther = @($wallIds | Where-Object { $idsThis -notcontains $_ })

  Write-Host ("[Type] {0}: walls={1}" -f $stem, (@($idsThis).Count)) -ForegroundColor Cyan
  if($nonwallIds.Count -gt 0){
    foreach($batch in (Chunk $nonwallIds 200)){
      Invoke-McpDurable -Port $Port -Method 'hide_elements_in_view' -Params @{ viewId=$viewId; elementIds=$batch } -Force | Out-Null
    }
  }
  if($idsOther.Count -gt 0){
    foreach($batch in (Chunk $idsOther 200)){
      Invoke-McpDurable -Port $Port -Method 'hide_elements_in_view' -Params @{ viewId=$viewId; elementIds=$batch } -Force | Out-Null
    }
  }

  $fileBase = "walls_${stem}"
  $expParams = @{ viewId=$viewId; outputFolder=$OUTABS.Replace('\\','/'); fileName=$fileBase; dwgVersion='ACAD2018'; __smoke_ok=$true }
  $expRes = Invoke-McpDurable -Port $Port -Method 'export_dwg' -Params $expParams -WaitSec 300 -Force
  try { $expRes | ConvertTo-Json -Depth 20 | Out-File -FilePath (Join-Path $LOGS ("export_"+$stem+".json")) -Encoding utf8 } catch {}
  Invoke-McpDurable -Port $Port -Method 'reset_all_view_overrides' -Params @{ viewId=$viewId; __smoke_ok=$true } -Force | Out-Null

  $dwgPath = (Join-Path $OUTABS ($fileBase + '.dwg')).Replace('\\','/')
  # Prefer path from response if present
  try { if($expRes.result.result.path){ $dwgPath = [string]$expRes.result.result.path } } catch {}
  Start-Sleep -Milliseconds 300
  if(-not (Test-Path $dwgPath)){
    Write-Warning "DWG not found after export: $dwgPath"
  }
  $wallDwgs += @{ path=$dwgPath; stem=$stem }
}

# 3) Prepare AutoCAD merge payload (per-file rename of wall layers; non-walls come from seed)
$seedPath = (Resolve-Path (Join-Path $OutDir 'seed.dwg')).Path.Replace('\\','/')
$mergedDir = (Resolve-Path (Join-Path $OutDir 'Merged')).Path.Replace('\\','/')
$mergedOut = Join-Path $mergedDir 'merged.dwg'
$stagingRoot = (Resolve-Path $OutDir).Path.Replace('\\','/') + '/Staging'

$includeLayers = @('A-WALL-____-MCUT')
$rpc = @{
  jsonrpc='2.0'; id=1; method='merge_dwgs_perfile_rename'; params=@{
    inputs=$wallDwgs; output=$mergedOut.Replace('\\','/');
    rename=@{ include=$includeLayers; format='{old}_{stem}' };
    accore=@{ path=$AccorePath; seed=$seedPath; locale=$Locale; timeoutMs=$AccoreTimeoutMs };
    postProcess=@{ layTransDws=$null; purge=$true; audit=$true };
    stagingPolicy=@{ root=$stagingRoot; keepTempOnError=$true; atomicWrite=$true }
  }
}

$cmdPath = Join-Path $OutDir 'command.txt'
$rpc | ConvertTo-Json -Depth 50 | Out-File -FilePath $cmdPath -Encoding utf8
Write-Host ("[AutoCAD] merge payload saved: {0}" -f $cmdPath) -ForegroundColor Green

if($AutoMerge){
  try{
    $health = Invoke-WebRequest -Uri 'http://127.0.0.1:5251/health' -Method Get -TimeoutSec 3 -UseBasicParsing
    if($health.StatusCode -eq 200){
      Write-Host '[AutoCAD] /rpc call' -ForegroundColor Green
      $body = (Resolve-Path $cmdPath).Path
      $resp = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:5251/rpc' -Body $body -ContentType 'application/json; charset=utf-8'
      $resp | ConvertTo-Json -Depth 50 | Out-File -FilePath (Join-Path $OutDir 'autocad_merge_result.json') -Encoding utf8
      Write-Host '[AutoCAD] merge requested; see autocad_merge_result.json' -ForegroundColor Green
    } else {
      Write-Warning 'AutoCadMcpServer health check failed; skipping AutoMerge.'
    }
  }catch{ Write-Warning "AutoMerge failed: $($_.Exception.Message)" }
}

Write-Host 'Done.' -ForegroundColor Green
