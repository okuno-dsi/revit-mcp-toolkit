# @feature: Resolve default logs folder under Projects/<Project>_<Port>/Logs if not provided | keywords: スペース, ビュー, レベル
param(
  [int]$Port = 5210,
  [string]$OutDir,
  [int]$WaitSec = 120,
  [int]$JobTimeoutSec = 180
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8 = '1'
try { $utf8 = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $utf8; $OutputEncoding = $utf8 } catch {}

$SCRIPT_DIR = Split-Path -Parent $MyInvocation.MyCommand.Path
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'

# Resolve default logs folder under Projects/<Project>_<Port>/Logs if not provided
function Resolve-LogsDir([int]$p){
  $workRoot = Resolve-Path (Join-Path $SCRIPT_DIR '..\\..\\..\\Projects')
  $cands = Get-ChildItem -LiteralPath $workRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*_$p" }
  $chosen = $null
  if($cands){ $chosen = ($cands | Where-Object { $_.Name -notlike 'Project_*' } | Select-Object -First 1); if(-not $chosen){ $chosen = $cands | Select-Object -First 1 } }
  if(-not $chosen){ $chosen = New-Item -ItemType Directory -Path (Join-Path $workRoot ("Project_{0}" -f $p)) }
  $logs = Join-Path $chosen.FullName 'Logs'
  if(-not (Test-Path $logs)){ New-Item -ItemType Directory -Path $logs | Out-Null }
  return $logs
}

function Call-Mcp {
  param([string]$Method,[hashtable]$Params,[int]$Wait=$WaitSec,[int]$JobSec=$JobTimeoutSec)
  $pjson = ($Params | ConvertTo-Json -Depth 60 -Compress)
  $args = @('--port', $Port, '--command', $Method, '--params', $pjson, '--wait-seconds', [string]$Wait)
  if($JobSec -gt 0){ $args += @('--timeout-sec', [string]$JobSec) }
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

function Get-ViewId {
  $cv = Call-Mcp 'get_current_view' @{} 60 120
  foreach($path in 'result.result.viewId','result.viewId','viewId'){
    try{ $cur=$cv; foreach($seg in $path.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; return [int]$cur }catch{}
  }
  throw 'Could not resolve current viewId from response.'
}

function Get-VisibleIds([int]$viewId){
  $shape = @{ idsOnly = $true; page = @{ limit = 200000 } }
  $filter = @{}
  $res = Call-Mcp 'get_elements_in_view' @{ viewId=$viewId; _shape=$shape; _filter=$filter } 180 240
  foreach($path in 'result.result.elementIds','result.elementIds','elementIds'){
    try{ $cur=$res; foreach($seg in $path.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; return @($cur | ForEach-Object { [int]$_ }) }catch{}
  }
  return @()
}

function Get-HiddenRows([int]$viewId){
  # ask for explicit + category-level hidden (ids and category info)
  $filter = @{ includeKinds = @('explicit','category'); onlyRevealables = $true }
  $shape = @{ idsOnly = $false; page = @{ limit = 500000 } }
  $res = Call-Mcp 'audit_hidden_in_view' @{ viewId=$viewId; _filter=$filter; _shape=$shape } 180 240
  foreach($path in 'result.result.hiddenElements','result.hiddenElements','hiddenElements'){
    try{ $cur=$res; foreach($seg in $path.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; return @($cur) }catch{}
  }
  return @()
}

function Get-ElementInfo([int[]]$ids){
  $map = @{}
  if(-not $ids -or $ids.Count -eq 0){ return $map }
  $chunk = 200
  for($i=0;$i -lt $ids.Count;$i+=$chunk){
    $batch = @($ids[$i..([Math]::Min($i+$chunk-1,$ids.Count-1))])
    $res = Call-Mcp 'get_element_info' @{ elementIds=$batch; rich=$false } 180 240
    foreach($path in 'result.result.elements','result.elements','elements'){
      try{ $cur=$res; foreach($seg in $path.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; foreach($e in $cur){ try{ $map[[int]$e.elementId] = [string]$e.category }catch{} }; break }catch{}
    }
  }
  return $map
}

if(-not $OutDir -or [string]::IsNullOrWhiteSpace($OutDir)){ $OutDir = Resolve-LogsDir -p $Port }
if(-not (Test-Path $OutDir)){ New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
$viewId = Get-ViewId
Write-Host ("[View] id={0}" -f $viewId)

$visibleIds = Get-VisibleIds -viewId $viewId
Write-Host ("[Visible] count={0}" -f $visibleIds.Count)

$hiddenRows = Get-HiddenRows -viewId $viewId
$hiddenIds = @($hiddenRows | ForEach-Object { try { [int]$_.'elementId' } catch { $null } } | Where-Object { $_ -ne $null } | Select-Object -Unique)
Write-Host ("[Hidden] count={0}" -f $hiddenIds.Count)

$allIds = @($visibleIds + $hiddenIds | Select-Object -Unique)
$infoMap = Get-ElementInfo -ids $allIds

$elements = @()
foreach($id in $visibleIds){
  $cat = $infoMap[$id]; if(-not $cat){ $cat = '' }
  $elements += [pscustomobject]@{ elementId = $id; category = $cat; visible = $true; reason = $null }
}
foreach($row in $hiddenRows){
  try{
    $id = [int]$row.elementId
    $cat = $row.categoryName
    if(-not $cat){ $cat = $infoMap[$id] }
    $reason = $row.reason
    $elements += [pscustomobject]@{ elementId = $id; category = [string]$cat; visible = $false; reason = [string]$reason }
  }catch{}
}

$payload = [ordered]@{
  ok = $true
  viewId = $viewId
  counts = @{ visible = $visibleIds.Count; hidden = $hiddenIds.Count; total = $elements.Count }
  generatedAt = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
  elements = $elements | Sort-Object elementId
}

$outPath = Join-Path $OutDir ("view_elements_visibility_{0}_{1}.json" -f $viewId,(Get-Date -Format 'yyyyMMdd_HHmmss'))
($payload | ConvertTo-Json -Depth 100) | Out-File -FilePath $outPath -Encoding utf8

Write-Host ("Saved: {0}" -f $outPath) -ForegroundColor Green
Write-Output ($outPath)


