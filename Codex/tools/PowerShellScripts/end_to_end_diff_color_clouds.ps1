# @feature: end to end diff color clouds | keywords: スペース, ビュー, スナップショット
param(
  [int]$LeftPort = 5210,
  [int]$RightPort = 5211,
  [string]$BaseViewName = 'RSL1',
  [int]$ColorR = 154,
  [int]$ColorG = 205,
  [int]$ColorB = 50,
  [int]$Transparency = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8='1'

$PY = Join-Path $PSScriptRoot 'send_revit_command_durable.py'

function Call-Mcp {
  param([int]$Port,[string]$Method,[hashtable]$Params,[int]$Wait=240,[int]$Job=1200,[switch]$Force)
  $pjson = ($Params | ConvertTo-Json -Depth 60 -Compress)
  $args = @('--port', $Port, '--command', $Method, '--params', $pjson, '--wait-seconds', [string]$Wait)
  if($Job -gt 0){ $args += @('--timeout-sec', [string]$Job) }
  if($Force){ $args += '--force' }
  $tmp = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ('mcp_'+[System.IO.Path]::GetRandomFileName()+'.json'))
  $args += @('--output-file', $tmp)
  $null = & python -X utf8 $PY @args 2>$null
  $txt = ''
  try { $txt = Get-Content -LiteralPath $tmp -Raw -Encoding UTF8 } catch {}
  if(Test-Path $tmp){ Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
  if([string]::IsNullOrWhiteSpace($txt)){ throw "Empty MCP response ($Method)" }
  return ($txt | ConvertFrom-Json -Depth 400)
}

function Payload($obj){ if($obj.result -and $obj.result.result){ return $obj.result.result } elseif($obj.result){ return $obj.result } else { return $obj } }

function Prepare-View([int]$Port,[string]$Name){
  try { $null = Call-Mcp $Port 'open_views' @{ names=@($Name) } 60 120 -Force } catch {}
  $lov = Payload (Call-Mcp $Port 'list_open_views' @{} 60 120 -Force)
  $v = @($lov.views) | Where-Object { $_.name -eq $Name } | Select-Object -First 1
  if(-not $v){ throw ("Port {0}: view '{1}' not found" -f $Port, $Name) }
  $vid = [int]$v.viewId
  try { $null = Call-Mcp $Port 'activate_view' @{ viewId=$vid } 60 120 -Force } catch {}
  try { $null = Call-Mcp $Port 'show_all_in_view' @{ viewId=$vid; detachViewTemplate=$true; includeTempReset=$true; unhideElements=$true; clearElementOverrides=$false; batchSize=5000; startIndex=0; refreshView=$true } 180 600 -Force } catch {}
  return $vid
}

function Snapshot([int]$Port){
  $script = Join-Path $PSScriptRoot 'create_structural_details_snapshot.ps1'
  & pwsh -NoProfile -ExecutionPolicy Bypass -File $script -Port $Port -DeleteOld | Out-Null
}

function Strict-Diff([string]$Ljson,[string]$Rjson){
  $ts = Get-Date -Format 'yyyyMMdd_HHmmss'
  $root = (Get-Location).Path
  $csv   = Join-Path $root ("Work/crossport_differences_RSL1_"+$ts+".csv")
  $pairs = Join-Path $root ("Work/crossport_modified_pairs_RSL1_"+$ts+".json")
  $lids  = Join-Path $root ("Work/crossport_left_ids_RSL1_"+$ts+".json")
  $rids  = Join-Path $root ("Work/crossport_right_ids_RSL1_"+$ts+".json")
  $keys='familyName,typeName,符号,H,B,tw,tf,Type Mark,コメント,構造用途,材質'
  $null = & python -X utf8 (Join-Path $PSScriptRoot 'strict_crossport_diff.py') "$Ljson" "$Rjson" --csv "$csv" --left-ids "$lids" --right-ids "$rids" --pairs-out "$pairs" --pos-tol-mm 600 --len-tol-mm 150 --keys "$keys"
  return @{ csv=$csv; pairs=$pairs; leftIds=$lids; rightIds=$rids }
}

function Ensure-DiffView([int]$Port,[int]$BaseVid,[string]$Desired){
  # Try to find existing by name
  $lov = Payload (Call-Mcp $Port 'list_open_views' @{} 60 120 -Force)
  $v = @($lov.views) | Where-Object { $_.name -eq $Desired } | Select-Object -First 1
  if($v){ return [int]$v.viewId }
  # Duplicate directly via API
  $idem = ("dup:{0}:{1}" -f $BaseVid, $Desired)
  $dup = Payload (Call-Mcp $Port 'duplicate_view' @{ viewId=$BaseVid; withDetailing=$true; desiredName=$Desired; onNameConflict='returnExisting'; idempotencyKey=$idem } 180 360 -Force)
  $nv = 0; try { $nv = [int]$dup.viewId } catch { try { $nv = [int]$dup.newViewId } catch { $nv = 0 } }
  if($nv -le 0){ throw ("Port {0}: duplicate_view did not return viewId" -f $Port) }
  try { $null = Call-Mcp $Port 'set_view_template' @{ viewId=$nv; clear=$true } 60 120 -Force } catch {}
  return $nv
}

function Apply-Color([int]$Port,[int]$ViewId,[int[]]$Ids){
  if(-not $Ids -or $Ids.Count -eq 0){ return }
  $batch=200
  for($i=0;$i -lt $Ids.Count;$i+=$batch){
    $chunk = @($Ids[$i..([Math]::Min($i+$batch-1,$Ids.Count-1))])
    $param = @{ viewId=$ViewId; elementIds=$chunk; r=$ColorR; g=$ColorG; b=$ColorB; transparency=$Transparency } | ConvertTo-Json -Compress
    try { $null = Call-Mcp $Port 'set_visual_override' ($param | ConvertFrom-Json) 240 1200 -Force } catch {}
  }
}

function Create-Clouds([int]$Port,[int]$ViewId,[object[]]$Pairs,[string]$Side){
  $created = 0
  foreach($p in $Pairs){
    $eid = 0
    if($Side -eq 'left'){ try { $eid = [int]$p.leftId } catch {} } else { try { $eid = [int]$p.rightId } catch {} }
    if($eid -le 0){ continue }
    $param = @{ viewId=$ViewId; elementId=$eid; paddingMm=150; preZoom='element'; restoreZoom=$false; focusMarginMm=150; mode='aabb' } | ConvertTo-Json -Compress
    try {
      $res = Call-Mcp $Port 'create_revision_cloud_for_element_projection' ($param | ConvertFrom-Json) 180 600 -Force | Payload
      $cid = 0; try { $cid = [int]$res.cloudId } catch { $cid = 0 }
      if($cid -le 0){ continue }
      $created++
      $diffs = @(); foreach($d in @($p.diffs)){ try { $diffs += ("{0}: {1} -> {2}" -f $d.key,$d.left,$d.right) } catch {} }
      $comment = if($diffs.Count -gt 0){ 'Diff: ' + ($diffs -join '; ') } else { 'Diff' }
      try { $null = Call-Mcp $Port 'set_revision_cloud_parameter' @{ elementId=$cid; paramName='Comments'; value=$comment } 60 120 -Force } catch {}
    } catch {}
  }
  return $created
}

Write-Host "[1/5] Preparing views..." -ForegroundColor Cyan
$Lbase = Prepare-View -Port $LeftPort -Name $BaseViewName
$Rbase = Prepare-View -Port $RightPort -Name $BaseViewName

Write-Host "[2/5] Snapshots..." -ForegroundColor Cyan
Snapshot -Port $LeftPort
Snapshot -Port $RightPort
$leftSnap = (Get-ChildItem -Path (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path 'Work') -Recurse -File -Filter ("structural_details_port{0}_*.json" -f $LeftPort) | Sort-Object LastWriteTime | Select-Object -Last 1).FullName
$rightSnap= (Get-ChildItem -Path (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path 'Work') -Recurse -File -Filter ("structural_details_port{0}_*.json" -f $RightPort) | Sort-Object LastWriteTime | Select-Object -Last 1).FullName

Write-Host "[3/5] Diff..." -ForegroundColor Cyan
$res = Strict-Diff -Ljson $leftSnap -Rjson $rightSnap
$pairsPath = $res.pairs
$pairsObj = Get-Content -LiteralPath $pairsPath -Raw -Encoding UTF8 | ConvertFrom-Json -Depth 400
$idsL = @(); $idsR = @(); foreach($p in $pairsObj){ try{ $idsL += [int]$p.leftId }catch{}; try{ $idsR += [int]$p.rightId }catch{} }
$idsL = @($idsL | Sort-Object -Unique); $idsR = @($idsR | Sort-Object -Unique)

Write-Host "[4/5] Ensure Diff views..." -ForegroundColor Cyan
$Ldiff = Ensure-DiffView -Port $LeftPort -BaseVid $Lbase -Desired ($BaseViewName+' Diff')
$Rdiff = Ensure-DiffView -Port $RightPort -BaseVid $Rbase -Desired ($BaseViewName+' Diff')

Write-Host "[5/5] Color + Clouds..." -ForegroundColor Cyan
Apply-Color -Port $LeftPort -ViewId $Ldiff -Ids $idsL
Apply-Color -Port $RightPort -ViewId $Rdiff -Ids $idsR
$cL = Create-Clouds -Port $LeftPort -ViewId $Ldiff -Pairs $pairsObj -Side 'left'
$cR = Create-Clouds -Port $RightPort -ViewId $Rdiff -Pairs $pairsObj -Side 'right'

$summary = [pscustomobject]@{
  ok = $true
  leftPort = $LeftPort
  rightPort = $RightPort
  baseViewName = $BaseViewName
  leftDiffViewId = $Ldiff
  rightDiffViewId = $Rdiff
  coloredLeft = $idsL.Count
  coloredRight = $idsR.Count
  cloudsLeft = $cL
  cloudsRight = $cR
  pairsFile = $pairsPath
  csvFile = $res.csv
}

$sumPath = Join-Path (Get-Location).Path ("Work/end_to_end_summary_"+(Get-Date -Format 'yyyyMMdd_HHmmss')+".json")
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $sumPath -Encoding UTF8
Write-Host ("[Done] Summary -> " + $sumPath) -ForegroundColor Green
