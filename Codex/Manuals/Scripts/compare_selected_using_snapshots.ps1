param(
  [string]$LeftSnapshot,
  [string]$RightSnapshot,
  [int]$LeftPort = 5210,
  [int]$RightPort = 5211,
  [int]$RightElementId = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8='1'
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

$SCRIPT_DIR = $PSScriptRoot
$ROOT = (Resolve-Path (Join-Path $SCRIPT_DIR '..\..')).Path
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'

function LatestSnap([int]$Port){
  $pattern = "structural_details_port{0}_*.json" -f $Port
  $f = Get-ChildItem -Recurse -File -Filter $pattern (Join-Path $ROOT 'Work') | Sort-Object LastWriteTime | Select-Object -Last 1
  if(-not $f){ throw "Snapshot not found for port $Port" }
  return $f.FullName
}

if([string]::IsNullOrWhiteSpace($LeftSnapshot)){ $LeftSnapshot = LatestSnap -Port $LeftPort }
if([string]::IsNullOrWhiteSpace($RightSnapshot)){ $RightSnapshot = LatestSnap -Port $RightPort }

function LoadJson([string]$p){ return (Get-Content -LiteralPath $p -Raw -Encoding UTF8 | ConvertFrom-Json) }
$L = LoadJson $LeftSnapshot
$R = LoadJson $RightSnapshot

function GetSelectedId([int]$Port){
  $args = @('--port',$Port,'--command','get_selected_element_ids','--params','{}','--wait-seconds','60','--timeout-sec','120')
  $tmp = [System.IO.Path]::GetTempFileName()
  $args += @('--output-file',$tmp)
  $null = & python -X utf8 $PY @args 2>$null
  $txt=''; try { $txt = Get-Content -LiteralPath $tmp -Raw -Encoding UTF8 } catch {}
  if(Test-Path $tmp){ Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
  if([string]::IsNullOrWhiteSpace($txt)){ throw "Empty response from MCP (get_selected_element_ids)" }
  $obj = $txt | ConvertFrom-Json -Depth 100
  foreach($path in 'result.result.elementIds','result.elementIds','elementIds'){
    try{ $cur=$obj; foreach($seg in $path.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $arr=@($cur); if($arr.Count -gt 0){ return [int]$arr[0] } } catch {}
  }
  return 0
}

function CentroidMm($e){
  $bb = $e.bboxMm
  if($bb){ try { return @{ x=0.5*([double]$bb.min.x + [double]$bb.max.x); y=0.5*([double]$bb.min.y + [double]$bb.max.y); z=0.5*([double]$bb.min.z + [double]$bb.max.z) } } catch {} }
  $cm = $e.coordinatesMm; if($cm){ return @{ x=[double]$cm.x; y=[double]$cm.y; z=[double]$cm.z } }
  return @{ x=0.0; y=0.0; z=0.0 }
}
function Dist($a,$b){ $dx=[double]$a.x-[double]$b.x; $dy=[double]$a.y-[double]$b.y; $dz=[double]$a.z-[double]$b.z; return [Math]::Sqrt($dx*$dx+$dy*$dy+$dz*$dz) }

$rid = if($RightElementId -gt 0){ [int]$RightElementId } else { GetSelectedId -Port $RightPort }
if($rid -le 0){ throw 'RightPort: 選択が取得できません' }

$re = $null
foreach($e in @($R.elements)){ if([int]$e.elementId -eq $rid){ $re=$e; break } }
if(-not $re){
  # fallback to live info
  $info = & python -X utf8 $PY --port $RightPort --command get_element_info --params (@{ elementIds=@($rid); rich=$true } | ConvertTo-Json -Compress) --wait-seconds 120 --timeout-sec 480 2>$null | ConvertFrom-Json -Depth 200
  foreach($path in 'result.result.elements','result.elements','elements'){
    try{ $cur=$info; foreach($seg in $path.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $re = @($cur)[0]; break } catch {}
  }
}
if(-not $re){ throw "右側要素情報が取得できません (id=$rid)" }
$rc = CentroidMm $re

# find nearest in left snapshot
$best = @{ id=0; dist=[double]::PositiveInfinity; elem=$null; centroid=$null }
foreach($e in @($L.elements)){
  try{ $cc = CentroidMm $e; $d = Dist $cc $rc; if($d -lt $best.dist){ $best.id=[int]$e.elementId; $best.dist=$d; $best.elem=$e; $best.centroid=$cc } } catch {}
}
if($best.id -le 0){ throw '左側スナップショットに候補がありません' }

function MapTypeParams($snap,$typeId){
  $m=@{}; if(-not $snap.typeParameters){ return $m }
  $key = [string]$typeId; $tp = $snap.typeParameters.$key; if(-not $tp){ $tp = $snap.typeParameters[$typeId] }
  if($tp){ if($tp.params){ foreach($k in $tp.params.PSObject.Properties.Name){ $m[$k]=[string]$tp.params.$k } }; if($tp.display){ foreach($k in $tp.display.PSObject.Properties.Name){ if(-not $m.ContainsKey($k)){ $m[$k]=[string]$tp.display.$k } } } }
  return $m
}

$ltp = MapTypeParams $L ([int]$best.elem.typeId)
$rtp = MapTypeParams $R ([int]$re.typeId)

$keys = @('familyName','typeName','符号','H','B','tw','tf')
$diffs = @()
foreach($k in $keys){
  if($k -in @('familyName','typeName')){
    $lv = [string]$best.elem.$k; $rv = [string]$re.$k; if($lv -ne $rv){ $diffs += [pscustomobject]@{ key=$k; left=$lv; right=$rv } }
  } else {
    $lv = $ltp[$k]; $rv = $rtp[$k]; if($lv -and $rv -and $lv -ne $rv){ $diffs += [pscustomobject]@{ key=$k; left=$lv; right=$rv } }
  }
}

$report = [pscustomobject]@{
  ok = $true
  left  = @{ port=$LeftPort; elementId=$best.id; categoryId=[int]$best.elem.categoryId; familyName=[string]$best.elem.familyName; typeName=[string]$best.elem.typeName; typeId=[int]$best.elem.typeId; centroidMm=$best.centroid }
  right = @{ port=$RightPort; elementId=[int]$re.elementId;   categoryId=[int]$re.categoryId;   familyName=[string]$re.familyName;   typeName=[string]$re.typeName;   typeId=[int]$re.typeId;   centroidMm=$rc }
  distanceMm = [Math]::Round($best.dist,3)
  differences = $diffs
}

$ts = Get-Date -Format 'yyyyMMdd_HHmmss'
$outJ = Join-Path $ROOT ("Work/compare_selected_pair_"+$ts+".json")
$outC = Join-Path $ROOT ("Work/compare_selected_pair_"+$ts+".csv")
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $outJ -Encoding UTF8
$rows = @(); foreach($d in $diffs){ $rows += [pscustomobject]@{ key=$d.key; left=$d.left; right=$d.right } }; $rows | Export-Csv -LiteralPath $outC -NoTypeInformation -Encoding UTF8

Write-Host ("Saved: JSON="+$outJ+" CSV="+$outC) -ForegroundColor Green
$report | ConvertTo-Json -Depth 8
