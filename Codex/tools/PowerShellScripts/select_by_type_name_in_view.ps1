# @feature: select by type name in view | keywords: スペース, ビュー
param(
  [Parameter(Mandatory=$true)][string]$TypeName,
  [int]$Port = 5210,
  [int[]]$CategoryIds,
  [switch]$AllCategories,
  [switch]$Contains,
  [string]$ProjectName = 'Select_By_TypeName',
  [switch]$Append
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8 = '1'
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

$SCRIPT_DIR = $PSScriptRoot
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'
if(!(Test-Path $PY)) { Write-Error "Python client not found: $PY"; exit 2 }

function Ensure-ProjectDir([string]$baseName, [int]$p){
  $workRoot = Resolve-Path (Join-Path $SCRIPT_DIR '..\\..\\..\\Projects')
  $projName = ("{0}_{1}" -f $baseName, $p)
  $projDir = Join-Path $workRoot $projName
  if(!(Test-Path $projDir)){ [void](New-Item -ItemType Directory -Path $projDir) }
  $logs = Join-Path $projDir 'Logs'
  if(!(Test-Path $logs)){ [void](New-Item -ItemType Directory -Path $logs) }
  return @{ Root = $projDir; Logs = $logs }
}

function Get-Payload($jsonObj){
  if($null -ne $jsonObj.result){
    if($null -ne $jsonObj.result.result){ return $jsonObj.result.result }
    return $jsonObj.result
  }
  return $jsonObj
}

function Invoke-Revit($method, $paramsObj, $outFile){
  $paramsJson = if($null -ne $paramsObj) { ($paramsObj | ConvertTo-Json -Depth 60 -Compress) } else { '{}' }
  python -X utf8 $PY --port $Port --command $method --params $paramsJson --output-file $outFile | Out-Null
  if(!(Test-Path $outFile)){ throw "Expected output file not found: $outFile" }
  return (Get-Content -Raw -Encoding UTF8 -Path $outFile | ConvertFrom-Json)
}

function Get-ActiveViewId(){
  $out = Join-Path $dirs.Logs 'current_view.json'
  $cv = Invoke-Revit -method 'get_current_view' -paramsObj @{ } -outFile $out
  $b = Get-Payload $cv
  $vid = 0
  try { $vid = [int]$b.viewId } catch {}
  if($vid -le 0){ throw "Invalid active viewId ($vid)" }
  return $vid
}

function Get-IdsInView([int]$viewId, [int[]]$incCats){
  $shape = @{ idsOnly = $true; page = @{ limit = 200000 } }
  $params = @{ viewId = $viewId; _shape = $shape }
  if($incCats -and $incCats.Count -gt 0){ $params['_filter'] = @{ includeCategoryIds = @($incCats) } }
  $out = Join-Path $dirs.Logs 'elements_in_view_ids.json'
  $resp = Invoke-Revit -method 'get_elements_in_view' -paramsObj $params -outFile $out
  $b = Get-Payload $resp
  $ids = @()
  try { $ids = @($b.elementIds | ForEach-Object { [int]$_ }) } catch { $ids = @() }
  return $ids
}

function Resolve-TypeName($e){
  $tn = ''
  try { $tn = [string]$e.typeName } catch {}
  if([string]::IsNullOrWhiteSpace($tn)){
    try { $tn = [string]$e.symbol.name } catch {}
  }
  if([string]::IsNullOrWhiteSpace($tn)){
    try { $tn = [string]$e.type.name } catch {}
  }
  if([string]::IsNullOrWhiteSpace($tn)){
    try { $tn = [string]$e.parameters.'Type Name'.value } catch {}
  }
  return $tn
}

function Match-TypeName([string]$candidate, [string]$target, [switch]$contains){
  if([string]::IsNullOrWhiteSpace($candidate)){ return $false }
  if($contains){ return $candidate -like ('*'+$target+'*') }
  return ($candidate -eq $target)
}

$dirs = Ensure-ProjectDir -baseName $ProjectName -p $Port
Write-Host ("[Dirs] Using {0}" -f $dirs.Root) -ForegroundColor DarkCyan

$viewId = Get-ActiveViewId
Write-Host ("[View] Active viewId={0}" -f $viewId) -ForegroundColor Cyan

if(-not $AllCategories.IsPresent -and (-not $CategoryIds -or $CategoryIds.Count -eq 0)){
  # Default to Structural Framing for performance
  $CategoryIds = @(-2001320)
}

$idsInView = Get-IdsInView -viewId $viewId -incCats $([int[]]$CategoryIds)
if($idsInView.Count -eq 0){ Write-Host '[Info] No elements found in the current view (given filters).' -ForegroundColor Yellow; exit 0 }
Write-Host ("[Candidates] Elements to inspect: {0}" -f $idsInView.Count) -ForegroundColor Gray

$matches = New-Object System.Collections.Generic.List[Int32]
$chunk = 200
for($i=0; $i -lt $idsInView.Count; $i += $chunk){
  $hi = [Math]::Min($i+$chunk-1,$idsInView.Count-1)
  $batch = @($idsInView[$i..$hi])
  $outInfo = Join-Path $dirs.Logs ("element_info_{0}_{1}.json" -f $i, $batch.Count)
  $info = Invoke-Revit -method 'get_element_info' -paramsObj @{ elementIds = $batch; rich = $true } -outFile $outInfo
  $b = Get-Payload $info
  $elems = @()
  foreach($p in 'elements','result.elements','result.result.elements'){
    try { $cur = $b | Select-Object -ExpandProperty $p -ErrorAction Stop; $elems = @($cur); break } catch {}
  }
  foreach($e in $elems){
    $tn = Resolve-TypeName $e
    if(Match-TypeName $tn $TypeName $Contains){
      try { [void]$matches.Add([int]$e.elementId) } catch {}
    }
  }
}

Write-Host ("[Filter] TypeName{0} '{1}': {2}" -f ($Contains.IsPresent ? ' contains' : ' =='), $TypeName, $matches.Count) -ForegroundColor Cyan
if($matches.Count -eq 0){ Write-Host '[Info] No matching elements found.' -ForegroundColor Yellow; exit 0 }

$selParams = @{ elementIds = @($matches); replace = (-not $Append.IsPresent) }
$outSel = Join-Path $dirs.Logs ('select_by_type_'+($TypeName.Replace(':','_'))+'.result.json')
$selResp = Invoke-Revit -method 'select_elements' -paramsObj $selParams -outFile $outSel
[void]$selResp

Write-Host ("[Done] Selected {0} elements by TypeName{1} '{2}'" -f $matches.Count, ($Contains.IsPresent ? ' contains' : ' =='), $TypeName) -ForegroundColor Green
Write-Host ("Result saved: {0}" -f $outSel) -ForegroundColor DarkGreen



