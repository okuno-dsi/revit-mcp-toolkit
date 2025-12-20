param(
  [int]$LeftPort = 5210,
  [int]$RightPort = 5211,
  [int[]]$CategoryIds = @(-2001320,-2001330),
  [int]$Chunk = 200
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8='1'
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

$SCRIPT_DIR = $PSScriptRoot
$ROOT = (Resolve-Path (Join-Path $SCRIPT_DIR '..\..')).Path
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'

function Call-Mcp { param([int]$Port,[string]$Method,[hashtable]$Params,[int]$W=240,[int]$T=1200,[switch]$Force)
  $pjson = ($Params | ConvertTo-Json -Depth 80 -Compress)
  $args = @('--port',$Port,'--command',$Method,'--params',$pjson,'--wait-seconds',[string]$W,'--timeout-sec',[string]$T)
  if($Force){ $args += '--force' }
  $tmp = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ('mcp_'+[System.IO.Path]::GetRandomFileName()+'.json'))
  $args += @('--output-file',$tmp)
  $null = & python -X utf8 $PY @args 2>$null
  $code=$LASTEXITCODE
  $txt=''; try{ $txt = Get-Content -LiteralPath $tmp -Raw -Encoding UTF8 } catch{}
  if(Test-Path $tmp){ Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
  if($code -ne 0){ throw "MCP failed ($Method): $txt" }
  if([string]::IsNullOrWhiteSpace($txt)){ throw "Empty MCP response ($Method)" }
  return ($txt | ConvertFrom-Json -Depth 400)
}

function Payload($obj){ if($obj.result -and $obj.result.result){ return $obj.result.result } elseif($obj.result){ return $obj.result } else { return $obj } }

function CentroidMm($e){
  $bb = $e.bboxMm
  if($bb){ try { return @{ x=0.5*([double]$bb.min.x + [double]$bb.max.x); y=0.5*([double]$bb.min.y + [double]$bb.max.y); z=0.5*([double]$bb.min.z + [double]$bb.max.z) } } catch {} }
  $cm = $e.coordinatesMm; if($cm){ return @{ x=[double]$cm.x; y=[double]$cm.y; z=[double]$cm.z } }
  return @{ x=0.0; y=0.0; z=0.0 }
}

function Dist($a,$b){ $dx=[double]$a.x-[double]$b.x; $dy=[double]$a.y-[double]$b.y; $dz=[double]$a.z-[double]$b.z; return [Math]::Sqrt($dx*$dx+$dy*$dy+$dz*$dz) }

# 1) Selection on 5211 (Right)
$sel = Payload (Call-Mcp $RightPort 'get_selected_element_ids' @{} 60 120 -Force)
$rid = 0; try { $rid = [int](@($sel.elementIds)[0]) } catch { throw '5211: 選択が見つかりません' }
$re = Payload (Call-Mcp $RightPort 'get_element_info' @{ elementIds=@($rid); rich=$true } 180 600 -Force)
$rightElem = @($re.elements)[0]; if(-not $rightElem){ throw "5211: 要素情報が取得できません (id=$rid)" }
$rc = CentroidMm $rightElem

# 2) Candidates on 5210 in current view (given categories)
$cvL = Payload (Call-Mcp $LeftPort 'get_current_view' @{} 60 120 -Force)
$lvid = [int]$cvL.viewId
$iev = Payload (Call-Mcp $LeftPort 'get_elements_in_view' @{ viewId=$lvid; categoryIds=$CategoryIds; _shape=@{ idsOnly=$true; page=@{ limit=200000 } }; _filter=@{ modelOnly=$true; excludeImports=$true } } 240 600 -Force)
$ids = @(); foreach($path in 'elementIds','result.elementIds','result.result.elementIds'){ try { $cur=$iev; foreach($seg in $path.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $ids=@($cur); break } catch {} }
$ids = @($ids | ForEach-Object { try { [int]$_ } catch { $null } } | Where-Object { $_ -ne $null })

$best = @{ id=0; dist=[double]::PositiveInfinity; elem=$null; centroid=$null }
for($i=0; $i -lt $ids.Count; $i+=$Chunk){
  $batch = @($ids[$i..([Math]::Min($i+$Chunk-1,$ids.Count-1))])
  $li = Payload (Call-Mcp $LeftPort 'get_element_info' @{ elementIds=$batch; rich=$true } 240 900 -Force)
  foreach($e in @($li.elements)){
    try {
      $cc = CentroidMm $e
      $d = Dist $cc $rc
      if($d -lt $best.dist){ $best.id=[int]$e.elementId; $best.dist=$d; $best.elem=$e; $best.centroid=$cc }
    } catch {}
  }
}
if($best.id -le 0){ throw '5210: 対になる候補が見つかりません' }

# 3) Type parameter comparison
$keys = @('符号','H','B','tw','tf') | ForEach-Object { @{ name = $_ } }

function Get-TypeParams([int]$Port,[int]$TypeId){
  if($TypeId -le 0){ return $null }
  $tp = Payload (Call-Mcp $Port 'get_type_parameters_bulk' @{ typeIds=@($TypeId); paramKeys=$keys; page=@{ startIndex=0; batchSize=100 } } 120 480 -Force)
  try { return @($tp.items)[0] } catch { return $null }
}

$ltid = 0; try { $ltid = [int]$best.elem.typeId } catch {}
$rtid = 0; try { $rtid = [int]$rightElem.typeId } catch {}
$ltp = Get-TypeParams -Port $LeftPort -TypeId $ltid
$rtp = Get-TypeParams -Port $RightPort -TypeId $rtid

function Map-TP($tp){ $m=@{}; if($tp){ if($tp.params){ foreach($k in $tp.params.PSObject.Properties.Name){ $m[$k]=[string]$tp.params.$k } }; if($tp.display){ foreach($k in $tp.display.PSObject.Properties.Name){ if(-not $m.ContainsKey($k)){ $m[$k]=[string]$tp.display.$k } } } }; return $m }
$mtpL = Map-TP $ltp; $mtpR = Map-TP $rtp

function Diff-Map($l,$r){ $diff=@(); foreach($k in $l.Keys){ if($r.ContainsKey($k)){ $lv=[string]$l[$k]; $rv=[string]$r[$k]; if($lv -ne $rv){ $diff += [pscustomobject]@{ key=$k; left=$lv; right=$rv } } } }; return $diff }
$typeDiffs = Diff-Map $mtpL $mtpR
if(([string]$best.elem.familyName) -ne ([string]$rightElem.familyName)){ $typeDiffs += [pscustomobject]@{ key='familyName'; left=([string]$best.elem.familyName); right=([string]$rightElem.familyName) } }
if(([string]$best.elem.typeName) -ne ([string]$rightElem.typeName)){ $typeDiffs += [pscustomobject]@{ key='typeName'; left=([string]$best.elem.typeName); right=([string]$rightElem.typeName) } }

# 4) Report
$dx = [double]$best.centroid.x - [double]$rc.x; $dy = [double]$best.centroid.y - [double]$rc.y; $dz = [double]$best.centroid.z - [double]$rc.z
$dist = [Math]::Sqrt($dx*$dx + $dy*$dy + $dz*$dz)

$report = [pscustomobject]@{
  ok = $true
  pair = @{ left = @{ port=$LeftPort; elementId=$best.id; familyName=[string]$best.elem.familyName; typeName=[string]$best.elem.typeName; typeId=[int]$best.elem.typeId; centroidMm=$best.centroid };
           right = @{ port=$RightPort; elementId=[int]$rightElem.elementId; familyName=[string]$rightElem.familyName; typeName=[string]$rightElem.typeName; typeId=[int]$rightElem.typeId; centroidMm=$rc } }
  distanceMm = [Math]::Round($dist,3)
  typeDifferences = $typeDiffs
}

$ts = Get-Date -Format 'yyyyMMdd_HHmmss'
$outj = Join-Path $ROOT ("Work/selected_5211_vs_5210_pair_"+$ts+".json")
$outc = Join-Path $ROOT ("Work/selected_5211_vs_5210_pair_"+$ts+".csv")
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $outj -Encoding UTF8
$rows = @(); foreach($d in $typeDiffs){ $rows += [pscustomobject]@{ key=$d.key; left=$d.left; right=$d.right } }
$rows | Export-Csv -LiteralPath $outc -NoTypeInformation -Encoding UTF8

Write-Host ("Saved: JSON=$outj CSV=$outc") -ForegroundColor Green
$report | ConvertTo-Json -Depth 8
