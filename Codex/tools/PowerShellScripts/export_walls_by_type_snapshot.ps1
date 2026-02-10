# @feature: Ensure UTF-8 for child process I/O and PowerShell parsing | keywords: 壁, スペース, ビュー, タグ, DWG, キャプチャ, スナップショット
param(
  [int]$Port = 5210,
  [string]$ProjectDir,
  [switch]$AutoMerge,
  [switch]$Smoke,            # run smoke_test before each write
  [switch]$Force,            # proceed on smoke warnings
  [int]$MaxWaitSec = 180,    # client-side max wait per command
  [int]$JobTimeoutSec = 180, # server-side job timeout per command
  [string]$AccorePath = "C:/Program Files/Autodesk/AutoCAD 2025/accoreconsole.exe",
  [string]$Locale = "en-US",
  [int]$AccoreTimeoutMs = 600000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8 = '1'
# Ensure UTF-8 for child process I/O and PowerShell parsing
try {
  $utf8NoBom = New-Object System.Text.UTF8Encoding $false
  [Console]::OutputEncoding = $utf8NoBom
  $OutputEncoding = $utf8NoBom
} catch {}

$ROOT = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$WORKROOT = Join-Path $ROOT 'Codex\Work'
if ([string]::IsNullOrWhiteSpace($ProjectDir)){
  $proj = Get-ChildItem -LiteralPath $WORKROOT -Directory -Filter 'Project_*' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if(-not $proj){ throw "No project folder under $WORKROOT. Specify -ProjectDir." }
  $ProjectDir = $proj.FullName
}
if(-not (Test-Path $ProjectDir)){ throw "ProjectDir not found: $ProjectDir" }
$OutDir = Join-Path $ProjectDir 'DWG'
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$MergedDir = Join-Path $OutDir 'Merged'
New-Item -ItemType Directory -Path $MergedDir -Force | Out-Null

$SCRIPT_DIR = $PSScriptRoot
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'
$LOGS = Resolve-Path (Join-Path $SCRIPT_DIR '..\Logs')

function Invoke-McpDurable {
  param([string]$Method,[hashtable]$Params,[double]$WaitSec=$MaxWaitSec,[switch]$Force,[int]$JobSec=$JobTimeoutSec)
  $pjson = ($Params | ConvertTo-Json -Depth 50 -Compress)
  $args = @('--port', $Port, '--command', $Method, '--params', $pjson, '--wait-seconds', [string]$WaitSec)
  if($JobSec -gt 0){ $args += @('--timeout-sec', [string]$JobSec) }
  if($Force){ $args += '--force' }
  $tmp = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ("mcp_"+[System.IO.Path]::GetRandomFileName()+".json"))
  $argsWithOut = $args + @('--output-file', $tmp)
  $null = & python -X utf8 $PY @argsWithOut 2>$null
  $code = $LASTEXITCODE
  try {
    $txt = Get-Content -LiteralPath $tmp -Raw -Encoding UTF8
  } catch {
    $txt = $null
  }
  if(Test-Path $tmp){ Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
  if($code -ne 0){ throw "MCP call failed ($Method): $txt" }
  if([string]::IsNullOrWhiteSpace($txt)){ throw "Empty response from MCP ($Method)" }
  return $txt | ConvertFrom-Json -Depth 100
}

# Safe wrapper: optional smoke_test preflight
function Invoke-McpSafe {
  param([string]$Method,[hashtable]$Params,[double]$WaitSec=$MaxWaitSec)
  if($Smoke){
    $sParams = @{ method = $Method; params = $Params }
    try {
      $s = Invoke-McpDurable 'smoke_test' $sParams -WaitSec ([Math]::Min(30,$WaitSec))
      # unwrap
      $ok=$true; $severity=''; $msg=''
      try { $ok = [bool]$s.result.result.ok } catch {}
      try { $severity = [string]$s.result.result.severity } catch {}
      try { $msg = [string]$s.result.result.msg } catch {}
      if(-not $ok){ throw "smoke_test failed: $msg" }
      if(($severity -eq 'warn') -and -not $Force){ throw "smoke_test warns: $msg (use -Force to proceed)" }
    } catch {
      throw $_
    }
    # mark as acknowledged
    $Params['__smoke_ok'] = $true
  }
  return (Invoke-McpDurable $Method $Params -WaitSec $WaitSec -Force:$Force)
}

function Get-CurrentViewId {
  $res = Invoke-McpDurable 'get_current_view' @{}
  foreach($path in 'result.result.viewId','result.viewId','viewId'){
    try{ $cur=$res; foreach($seg in $path.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; return [int]$cur }catch{}
  }
  throw 'Could not resolve current viewId'
}

function Get-IdsInView([int]$viewId,[int[]]$includeCatIds,[int[]]$excludeCatIds){
  # get all ids first (command does not support excludeCategoryIds)
  $shape = @{ idsOnly = $true; page = @{ limit = 800000 } }
  $params = @{ viewId=$viewId; _shape=$shape }
  $res = Invoke-McpDurable 'get_elements_in_view' $params
  $all = @()
  foreach($path in 'result.result.elementIds','result.elementIds','elementIds'){
    try{ $cur=$res; foreach($seg in $path.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $all=@($cur | ForEach-Object { [int]$_ }); break }catch{}
  }
  if(-not $all -or $all.Count -eq 0){ return @() }
  if(((!$includeCatIds) -or $includeCatIds.Count -eq 0) -and ((!$excludeCatIds) -or $excludeCatIds.Count -eq 0)){
    return $all
  }
  # enrich and filter by categoryId
  $rows = Get-ElementInfoBulk -ids $all -rich $true
  $keep = New-Object System.Collections.Generic.List[int]
  $inc = @(); if($includeCatIds){ $inc = @($includeCatIds | ForEach-Object { [int]$_ }) }
  $exc = @(); if($excludeCatIds){ $exc = @($excludeCatIds | ForEach-Object { [int]$_ }) }
  foreach($e in $rows){
    $cid = $null
    try{ $cid = [int]$e.categoryId }catch{}
    if($cid -eq $null){ continue }
    if($inc.Count -gt 0 -and ($inc -notcontains $cid)){ continue }
    if($exc.Count -gt 0 -and ($exc -contains $cid)){ continue }
    try{ [void]$keep.Add([int]$e.elementId) }catch{}
  }
  return @($keep)
}

function Get-ElementInfoBulk([int[]]$ids, [bool]$rich=$false){
  if(-not $ids -or $ids.Count -eq 0){ return @() }
  $rows = @()
  $chunk = 200
  for($i=0;$i -lt $ids.Count;$i+=$chunk){
    $batch = @($ids[$i..([Math]::Min($i+$chunk-1,$ids.Count-1))])
    $res = Invoke-McpDurable 'get_element_info' @{ elementIds=$batch; rich=$rich }
    foreach($path in 'result.result.elements','result.elements','elements'){
      try{ $cur=$res; foreach($seg in $path.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; if($cur){ $rows += $cur; break } }catch{}
    }
  }
  return @($rows)
}

function Chunk([object[]]$arr,[int]$size){ for($i=0;$i -lt $arr.Count;$i+=$size){ $len=[Math]::Min($size,$arr.Count-$i); $arr[$i..($i+$len-1)] } }

# Preflight helpers: verify visibility in the active view before export
function Preflight-Seed([int]$viewId,[int[]]$wallIds){
  Write-Host '[Preflight] Seed: hide walls then verify no walls visible' -ForegroundColor Yellow
  foreach($batch in (Chunk $wallIds 800)){
    Invoke-McpDurable 'hide_elements_in_view' @{ viewId=$viewId; elementIds=$batch; detachViewTemplate=$true; refreshView=$true; batchSize=800 } ([Math]::Min(60,$MaxWaitSec)) | Out-Null
  }
  $WALL_CAT = -2000011
  $wallsNow = Get-IdsInView -viewId $viewId -includeCatIds @($WALL_CAT) -excludeCatIds @()
  $wallsNowCount = (@($wallsNow)).Count
  if($wallsNowCount -ne 0){ throw "Preflight failed (seed): walls still visible count=$wallsNowCount" }
}

function Preflight-Type([int]$viewId,[int[]]$idsThis,[int[]]$nonwallIds,[int[]]$idsOther){
  Write-Host ("[Preflight] Type: idsThis={0} nonwalls={1} others={2}" -f $idsThis.Count, $nonwallIds.Count, $idsOther.Count) -ForegroundColor Yellow
  foreach($batch in (Chunk $nonwallIds 800)){
    Invoke-McpDurable 'hide_elements_in_view' @{ viewId=$viewId; elementIds=$batch; detachViewTemplate=$true; refreshView=$true; batchSize=800 } ([Math]::Min(60,$MaxWaitSec)) | Out-Null
  }
  foreach($batch in (Chunk $idsOther 800)){
    Invoke-McpDurable 'hide_elements_in_view' @{ viewId=$viewId; elementIds=$batch; detachViewTemplate=$true; refreshView=$true; batchSize=800 } ([Math]::Min(60,$MaxWaitSec)) | Out-Null
  }
  $WALL_CAT = -2000011
  $onlyWallsNow = Get-IdsInView -viewId $viewId -includeCatIds @($WALL_CAT) -excludeCatIds @()
  $onlyWallsNowCount = (@($onlyWallsNow)).Count
  $idsThisCount = (@($idsThis)).Count
  if($onlyWallsNowCount -ne $idsThisCount){ throw "Preflight failed (type): expected $idsThisCount walls, got $onlyWallsNowCount" }
}

$viewId = Get-CurrentViewId
Write-Host ("[View] id={0}" -f $viewId) -ForegroundColor Cyan

# Save view state
Write-Host '[Snapshot] save_view_state' -ForegroundColor Cyan
$snapshot = Invoke-McpDurable 'save_view_state' @{ viewId=$viewId } -WaitSec ([Math]::Min(60,$MaxWaitSec))
$state = $null; foreach($p in 'result.result.state','result.state','state'){ try{ $cur=$snapshot; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $state=$cur; break }catch{} }
if(-not $state){ Write-Warning 'Could not capture state'; }

# Collect wall ids and non-wall ids in current view
$WALL_CAT = -2000011
$wallIds    = Get-IdsInView -viewId $viewId -includeCatIds @($WALL_CAT) -excludeCatIds @() -ModelOnly:$false -ExcludeImports:$true
$nonwallIds = Get-IdsInView -viewId $viewId -includeCatIds @() -excludeCatIds @($WALL_CAT) -ModelOnly:$false -ExcludeImports:$true
# Normalize to arrays to ensure .Count is available
$wallIds = @($wallIds)
$nonwallIds = @($nonwallIds)
Write-Host ("[Counts] walls={0} nonwalls={1}" -f $wallIds.Count, $nonwallIds.Count)

# Group walls by type name
$info = Get-ElementInfoBulk -ids $wallIds
$groups = @{}
foreach($e in $info){
  $typeName = ''
  try{ $typeName = [string]$e.typeName }catch{}
  if([string]::IsNullOrWhiteSpace($typeName)){ $typeName = 'UNKNOWN' }
  $stem = ($typeName -replace '[^A-Za-z0-9_-]','_')
  if(-not $groups.ContainsKey($stem)){ $groups[$stem] = New-Object System.Collections.Generic.List[int] }
  try{ [void]$groups[$stem].Add([int]$e.elementId) }catch{}
}

$DoPreflightOnActive = $false
if($DoPreflightOnActive){
  # Preflight seed (no walls visible) with snapshot restore to preserve current view state
  try { Preflight-Seed -viewId $viewId -wallIds $wallIds } catch { Write-Warning $_ }
  if($state){ try{ Invoke-McpDurable 'restore_view_state' @{ viewId=$viewId; state=$state; apply=@{ template=$true; categories=$true; filters=$true; worksets=$true; hiddenElements=$true } } -Force | Out-Null }catch{ Write-Warning $_ } }
} else {
  Write-Host '[Preflight] Skipped active-view mutation' -ForegroundColor Yellow
}

# Export seed from duplicated working view by specifying non-wall elements directly
$outAbs = (Resolve-Path $OutDir).Path
Write-Host '[Seed] export non-walls only via elementIds' -ForegroundColor Cyan
$seedRes = Invoke-McpSafe 'export_dwg' @{ viewId=$viewId; outputFolder=$outAbs.Replace('\\','/'); fileName='seed'; dwgVersion='ACAD2018'; elementIds=@($nonwallIds) } ([Math]::Min(300,$MaxWaitSec))
try{ $seedRes | ConvertTo-Json -Depth 30 | Out-File -FilePath (Join-Path $PSScriptRoot '..\Logs\export_seed.json') -Encoding utf8 }catch{}

# For each type: show everything, then hide non-walls + other walls, export walls_<stem>.dwg
$exported = @()
foreach($stem in $groups.Keys){
  $idsThis = @($groups[$stem] | ForEach-Object { [int]$_ })
  $idsOther = @($wallIds | Where-Object { $idsThis -notcontains $_ })
  Write-Host ("[Type] {0}: {1} walls" -f $stem, $idsThis.Count) -ForegroundColor Cyan

  # Preflight skipped to avoid mutating the active view

  $fileBase = "walls_${stem}"
  # Export each wall type by passing its elementIds directly; ExportDwg duplicates the view and hides non-targets inside the duplicate
  $exp = Invoke-McpSafe 'export_dwg' @{ viewId=$viewId; outputFolder=$outAbs.Replace('\\','/'); fileName=$fileBase; dwgVersion='ACAD2018'; elementIds=@($idsThis) } ([Math]::Min(300,$MaxWaitSec))
  $path = Join-Path $outAbs ($fileBase + '.dwg')
  $exported += @{ path=$path.Replace('\\','/'); stem=$stem }
}

# Restore original view state if captured
if($state){
  try{ Invoke-McpDurable 'restore_view_state' @{ viewId=$viewId; state=$state; apply=@{ template=$true; categories=$true; filters=$true; worksets=$true; hiddenElements=$false } } -Force | Out-Null }catch{ Write-Warning $_ }
}

# Prepare AutoCAD merge payload
$seedPath = (Resolve-Path (Join-Path $OutDir 'seed.dwg')).Path.Replace('\\','/')
$mergedOut = (Join-Path $MergedDir 'walls_types_merged.dwg').Replace('\\','/')
$stagingRoot = $OutDir.Replace('\\','/') + '/Staging'
$includeLayers = @('A-WALL-____-MCUT')
$rpc = @{
  jsonrpc='2.0'; id=1; method='merge_dwgs_perfile_rename'; params=@{
    inputs=$exported; output=$mergedOut;
    rename=@{ include=$includeLayers; format='{old}_{stem}' };
    accore=@{ path=$AccorePath; seed=$seedPath; locale=$Locale; timeoutMs=$AccoreTimeoutMs };
    postProcess=@{ layTransDws=$null; purge=$true; audit=$true };
    stagingPolicy=@{ root=$stagingRoot; keepTempOnError=$true; atomicWrite=$true }
  }
}
$cmdPath = Join-Path $OutDir 'command.txt'
$rpc | ConvertTo-Json -Depth 40 | Out-File -FilePath $cmdPath -Encoding utf8
Write-Host ("[AutoCAD] merge payload saved: {0}" -f $cmdPath) -ForegroundColor Green

if($AutoMerge){
  try{
    $health = Invoke-WebRequest -Uri 'http://127.0.0.1:5251/health' -Method Get -TimeoutSec 3 -UseBasicParsing
    if($health.StatusCode -eq 200){
      $body = Get-Content -Raw $cmdPath
      $resp = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:5251/rpc' -Body $body -ContentType 'application/json; charset=utf-8'
      $resp | ConvertTo-Json -Depth 40 | Out-File -FilePath (Join-Path $OutDir 'autocad_merge_result.json') -Encoding utf8
      Write-Host '[AutoCAD] merge requested; see autocad_merge_result.json' -ForegroundColor Green
    } else { Write-Warning 'AutoCadMcpServer not healthy; skip AutoMerge.' }
  }catch{ Write-Warning "AutoMerge failed: $($_.Exception.Message)" }
}

Write-Host 'Done.' -ForegroundColor Green

