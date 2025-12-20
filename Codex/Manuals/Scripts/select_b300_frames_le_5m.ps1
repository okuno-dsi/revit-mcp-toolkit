param(
  [int]$Port = 5210,
  [string]$ProjectName = 'Select_B300_Le5m',
  [double]$MaxLengthMm = 5000,
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
  $workRoot = Resolve-Path (Join-Path $SCRIPT_DIR '..\..\Work')
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

function Get-StructuralFrameIdsInView([int]$viewId){
  # Use idsOnly + includeCategoryIds for Structural Framing (-2001320)
  $shape = @{ idsOnly = $true; page = @{ limit = 200000 } }
  $filter = @{ includeCategoryIds = @(-2001320) }
  $params = @{ viewId = $viewId; _shape = $shape; _filter = $filter }
  $out = Join-Path $dirs.Logs 'frames_in_view_ids.json'
  $resp = Invoke-Revit -method 'get_elements_in_view' -paramsObj $params -outFile $out
  $b = Get-Payload $resp
  $ids = @()
  try { $ids = @($b.elementIds | ForEach-Object { [int]$_ }) } catch { $ids = @() }
  return $ids
}

function Get-FramesInfo(){
  # get_structural_frames returns all frames when elementIds not reliably restrict; we will filter later
  $out = Join-Path $dirs.Logs 'structural_frames_all.json'
  $resp = Invoke-Revit -method 'get_structural_frames' -paramsObj @{ } -outFile $out
  $b = Get-Payload $resp
  $items = @()
  foreach($p in 'structuralFrames','frames'){
    try { $cur = $b | Select-Object -ExpandProperty $p -ErrorAction Stop; $items = @($cur); break } catch {}
  }
  return $items
}

function DistMm([double]$x1,[double]$y1,[double]$z1,[double]$x2,[double]$y2,[double]$z2){
  $dx = $x2 - $x1; $dy = $y2 - $y1; $dz = $z2 - $z1
  return [Math]::Sqrt($dx*$dx + $dy*$dy + $dz*$dz)
}

$dirs = Ensure-ProjectDir -baseName $ProjectName -p $Port
Write-Host ("[Dirs] Using {0}" -f $dirs.Root) -ForegroundColor DarkCyan

$viewId = Get-ActiveViewId
Write-Host ("[View] Active viewId={0}" -f $viewId) -ForegroundColor Cyan

$idsInView = Get-StructuralFrameIdsInView -viewId $viewId
if($idsInView.Count -eq 0){ Write-Host '[Info] No Structural Framing elements in the current view.' -ForegroundColor Yellow; exit 0 }
Write-Host ("[Frames] Candidates in view: {0}" -f $idsInView.Count) -ForegroundColor Gray

$allFrames = Get-FramesInfo
if($allFrames.Count -eq 0){ Write-Error 'get_structural_frames returned no items.'; exit 3 }

# Filter to only frames in the current view
$idSet = New-Object 'System.Collections.Generic.HashSet[int]'
foreach($i in $idsInView){ [void]$idSet.Add([int]$i) }

$b300Le5m = New-Object System.Collections.Generic.List[Int32]
foreach($f in $allFrames){
  $eid = 0; $tn = ''; $s=$null; $e=$null
  try { $eid = [int]$f.elementId } catch {}
  if($eid -le 0){ continue }
  if(-not $idSet.Contains($eid)){ continue }
  try { $tn = [string]$f.typeName } catch {}
  if([string]::IsNullOrWhiteSpace($tn)){ continue }
  if($tn -ne 'B300'){ continue }
  try { $s = $f.start; $e = $f.end } catch {}
  if($null -eq $s -or $null -eq $e){ continue }
  $len = DistMm ([double]$s.x) ([double]$s.y) ([double]$s.z) ([double]$e.x) ([double]$e.y) ([double]$e.z)
  if($len -le $MaxLengthMm + 0.5){ [void]$b300Le5m.Add($eid) }
}

Write-Host ("[Filter] B300 and length<= {0} mm: {1}" -f [int]$MaxLengthMm, $b300Le5m.Count) -ForegroundColor Cyan
if($b300Le5m.Count -eq 0){ Write-Host '[Info] No matching frames found.' -ForegroundColor Yellow; exit 0 }

# Select in Revit
$selParams = @{ elementIds = @($b300Le5m); replace = (-not $Append.IsPresent) }
$outSel = Join-Path $dirs.Logs 'select_b300_le5m.result.json'
$selResp = Invoke-Revit -method 'select_elements' -paramsObj $selParams -outFile $outSel
[void]$selResp # ignore contents; selection happens in UI

Write-Host ("[Done] Selected {0} Structural Frames (type=B300, length<= {1} mm)" -f $b300Le5m.Count, [int]$MaxLengthMm) -ForegroundColor Green
Write-Host ("Result saved: {0}" -f $outSel) -ForegroundColor DarkGreen

