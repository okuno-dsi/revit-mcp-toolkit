# @feature: Port from REVIT_MCP_PORT if not passed | keywords: スペース, ビュー
param(
  [int]$Port = 5210,
  [string]$Suffix = 'Copy',
  [ValidateSet('useExisting','increment','fail')][string]$OnConflict = 'useExisting',
  [switch]$WithDetailing,
  [switch]$ActivateNew,
  [switch]$DryRun,
  [int]$WaitSec = 180,
  [int]$JobTimeoutSec = 360
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8 = '1'
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

# Port from REVIT_MCP_PORT if not passed
$useEnv = $false
if(-not $PSBoundParameters.ContainsKey('Port') -and $env:REVIT_MCP_PORT){
  try { $Port = [int]$env:REVIT_MCP_PORT; $useEnv = $true } catch {}
}

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
  # Resolve active view id
  $cv = Invoke-Mcp 'get_current_view' @{} 60 120 -Force
  $vid = 0
  foreach($p in 'result.result.viewId','result.viewId','viewId'){ try{ $cur=$cv; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $vid=[int]$cur; break }catch{} }
  if($vid -le 0){ throw 'Could not resolve active viewId' }

  # Prefer list_open_views to get the human name
  $lov = Invoke-Mcp 'list_open_views' @{} 60 120 -Force
  $views = @(); $views = @(Get-JsonPath $lov @('result.result.views','result.views','views'))
  $name = ''
  foreach($v in $views){ try{ if([int]$v.viewId -eq $vid){ $name=[string]$v.name; break } }catch{} }
  if([string]::IsNullOrWhiteSpace($name)){
    # Fallback to get_view_info
    $vi = Invoke-Mcp 'get_view_info' @{ viewId=$vid } 60 120 -Force
    $name = [string](Get-JsonPath $vi @('result.result.view.name','result.result.name','result.name','name'))
  }
  if([string]::IsNullOrWhiteSpace($name)){ $name = ('View_'+$vid) }
  return @{ viewId=$vid; name=$name }
}

function Try-OpenViewByName([string]$name){
  if([string]::IsNullOrWhiteSpace($name)){ return 0 }
  try {
    $ov = Invoke-Mcp 'open_views' @{ names=@($name) } 60 120 -Force
  } catch { return 0 }
  # After open, list and resolve id
  try {
    $lov = Invoke-Mcp 'list_open_views' @{} 60 120 -Force
    $views = @(); $views = @(Get-JsonPath $lov @('result.result.views','result.views','views'))
    foreach($v in $views){ try{ if(([string]$v.name) -eq $name){ return [int]$v.viewId } }catch{} }
  } catch {}
  return 0
}

function Try-RenameView([int]$viewId,[string]$newName){
  if($viewId -le 0 -or [string]::IsNullOrWhiteSpace($newName)){ return $false }
  try {
    $r = Invoke-Mcp 'rename_view' @{ viewId=$viewId; newName=$newName } 60 120 -Force
    return $true
  } catch { return $false }
}

# Main flow
$active = Get-ActiveView
$origName = [string]$active.name
$origId = [int]$active.viewId

function Get-NormalizedBaseName([string]$name,[string]$suffix){
  if([string]::IsNullOrWhiteSpace($name)){ return 'View' }
  if([string]::IsNullOrWhiteSpace($suffix)){ return $name }
  # Strip trailing " <suffix>" or " <suffix> <n>" if present (idempotent base)
  try{
    $rx = [regex]::new(("^(.*)\s+"+[regex]::Escape($suffix)+"(?:\s+\d+)?$"))
    $m = $rx.Match($name)
    if($m.Success -and $m.Groups.Count -ge 2){ return $m.Groups[1].Value }
  }catch{}
  return $name
}

$baseName = Get-NormalizedBaseName -name $origName -suffix $Suffix
$desiredName = ($baseName + ' ' + $Suffix)

if($DryRun){
  $obj = [pscustomobject]@{ ok=$true; dryRun=$true; action='duplicate_once'; activeViewId=$origId; activeViewName=$origName; desiredName=$desiredName }
  $obj | ConvertTo-Json -Depth 5 -Compress
  exit 0
}

if($useEnv){ Write-Host "[Port] Using REVIT_MCP_PORT=$Port" -ForegroundColor DarkCyan }
Write-Host ("[Active] viewId={0} name='{1}'" -f $origId, $origName) -ForegroundColor Cyan

# 0) If a view with desiredName already exists, use it (no new duplicate)
$existingId = Try-OpenViewByName -name $desiredName
if($existingId -gt 0 -and $OnConflict -eq 'useExisting'){
  $out = [pscustomobject]@{
    ok = $true
    mode = 'existing'
    activeViewId = $origId
    activeViewName = $origName
    newViewId = $existingId
    newViewName = $desiredName
  }
  $out | ConvertTo-Json -Depth 5 -Compress
  exit 0
}

# 1) Duplicate once
$dupParams = @{ viewId=$origId; __smoke_ok=$true }
if($WithDetailing){ $dupParams['withDetailing'] = $true }
Write-Host '[Duplicate] duplicate_view' -ForegroundColor Cyan
$dup = Invoke-Mcp 'duplicate_view' $dupParams 120 240 -Force
$newViewId = 0
foreach($p in 'result.result.viewId','result.viewId','viewId'){ try{ $cur=$dup; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $newViewId=[int]$cur; break }catch{} }
if($newViewId -le 0){ throw 'duplicate_view did not return viewId' }

# 2) Rename to desiredName (conflict handling)
if(Try-RenameView -viewId $newViewId -newName $desiredName){
  if($ActivateNew){ try { $null = Invoke-Mcp 'activate_view' @{ viewId=$newViewId } 30 60 -Force } catch {} }
  $out = [pscustomobject]@{ ok=$true; mode='created'; activeViewId=$origId; activeViewName=$origName; newViewId=$newViewId; newViewName=$desiredName }
  $out | ConvertTo-Json -Depth 5 -Compress
  exit 0
}

switch ($OnConflict) {
  'useExisting' {
    # Try open existing target and delete the just-created duplicate
    $eid = Try-OpenViewByName -name $desiredName
    if($eid -gt 0){
      try { $null = Invoke-Mcp 'delete_view' @{ viewId=$newViewId; __smoke_ok=$true } 120 240 -Force } catch {}
      $out = [pscustomobject]@{ ok=$true; mode='existing'; activeViewId=$origId; activeViewName=$origName; newViewId=$eid; newViewName=$desiredName }
      $out | ConvertTo-Json -Depth 5 -Compress
      exit 0
    }
    # Fallback to increment if we could not find existing
    $OnConflict = 'increment'
  }
}

if($OnConflict -eq 'increment'){
  for($i=2; $i -le 20; $i++){
    $cand = ($desiredName + ' ' + $i)
    if(Try-RenameView -viewId $newViewId -newName $cand){
      if($ActivateNew){ try { $null = Invoke-Mcp 'activate_view' @{ viewId=$newViewId } 30 60 -Force } catch {} }
      $out = [pscustomobject]@{ ok=$true; mode='created-increment'; activeViewId=$origId; activeViewName=$origName; newViewId=$newViewId; newViewName=$cand }
      $out | ConvertTo-Json -Depth 5 -Compress
      exit 0
    }
  }
}

# If here, rename failed and policy was 'fail' or all increments failed
$dupName = ''
$dupName = [string](Get-JsonPath $dup @('result.result.name','result.name','name'))
$outFail = [pscustomobject]@{
  ok = $true
  mode = 'created-unnamed'
  note = 'Rename failed; keeping server-assigned name.'
  activeViewId = $origId
  activeViewName = $origName
  newViewId = $newViewId
  newViewName = $dupName
}
$outFail | ConvertTo-Json -Depth 5 -Compress
