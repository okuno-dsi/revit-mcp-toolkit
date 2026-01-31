# @feature: debug retry clouds 5210 | keywords: スペース, ビュー, タグ
param(
  [string]$CsvPath,
  [string]$BaseViewName = 'RSL1',
  [int]$Port = 5210,
  [double]$PaddingMm = 200.0,
  [double]$RectW1 = 800.0,
  [double]$RectH1 = 600.0,
  [double]$RectW2 = 1200.0,
  [double]$RectH2 = 900.0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8='1'
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

$SCRIPT_DIR = $PSScriptRoot
$ROOT = (Resolve-Path (Join-Path $SCRIPT_DIR '..\..')).Path
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'

function Call-Mcp { param([int]$P,[string]$Method,[hashtable]$Params,[int]$W=240,[int]$T=1200,[switch]$Force)
  $pjson = ($Params | ConvertTo-Json -Depth 120 -Compress)
  $args = @('--port',$P,'--command',$Method,'--params',$pjson,'--wait-seconds',[string]$W,'--timeout-sec',[string]$T)
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

function Resolve-CompareViewId([int]$P,[string]$Base){
  $Target = ($Base + ' 相違')
  try {
    $lov = Payload (Call-Mcp $P 'list_open_views' @{} 60 120 -Force)
    $v = @($lov.views) | Where-Object { $_.name -eq $Target } | Select-Object -First 1
    if(-not $v){ $v = @($lov.views) | Where-Object { $_.name -like ($Target+'*') } | Select-Object -First 1 }
    if($v){ $null = Call-Mcp $P 'activate_view' @{ viewId=[int]$v.viewId } 60 120 -Force; return [int]$v.viewId }
  } catch {}
  try { $null = Call-Mcp $P 'open_views' @{ names=@($Target) } 60 120 -Force } catch {}
  try {
    $lov2 = Payload (Call-Mcp $P 'list_open_views' @{} 60 120 -Force)
    $v2 = @($lov2.views) | Where-Object { $_.name -eq $Target } | Select-Object -First 1
    if($v2){ $null = Call-Mcp $P 'activate_view' @{ viewId=[int]$v2.viewId } 60 120 -Force; return [int]$v2.viewId }
  } catch {}
  try {
    $lov3 = Payload (Call-Mcp $P 'list_open_views' @{} 60 120 -Force)
    $base = @($lov3.views) | Where-Object { $_.name -eq $Base } | Select-Object -First 1
    if(-not $base){ try { $null = Call-Mcp $P 'open_views' @{ names=@($Base) } 60 120 -Force } catch {}; $lov3 = Payload (Call-Mcp $P 'list_open_views' @{} 60 120 -Force); $base = @($lov3.views) | Where-Object { $_.name -eq $Base } | Select-Object -First 1 }
    if($base){
      $dup = Payload (Call-Mcp $P 'duplicate_view' @{ viewId=[int]$base.viewId; withDetailing=$true; desiredName=$Target; onNameConflict='returnExisting'; idempotencyKey=("dup:{0}:{1}" -f $base.viewId,$Target) } 180 360 -Force)
      $vid=0; try { $vid=[int]$dup.viewId } catch { try { $vid=[int]$dup.newViewId } catch { $vid=0 } }
      if($vid -gt 0){ $null = Call-Mcp $P 'activate_view' @{ viewId=$vid } 60 120 -Force; return $vid }
    }
  } catch {}
  throw "Port ${P}: 相違ビューが見つかりません（'$Target'）"
}

if(-not (Test-Path -LiteralPath $CsvPath)){ throw "CSV not found: $CsvPath" }
$rows = Import-Csv -LiteralPath $CsvPath -Encoding UTF8 | Where-Object { try { [int]$_.'port' -eq $Port } catch { $false } }
$ids = @($rows | ForEach-Object { try { [int]$_.'elementId' } catch { $null } } | Where-Object { $_ -ne $null } | Sort-Object -Unique)

$vid = Resolve-CompareViewId -P $Port -Base $BaseViewName

# Reset visibility and sync
try { $null = Call-Mcp $Port 'set_view_template' @{ viewId=$vid; clear=$true } 60 120 -Force } catch {}
try { $null = Call-Mcp $Port 'show_all_in_view' @{ viewId=$vid; detachViewTemplate=$true; includeTempReset=$true; unhideElements=$true; clearElementOverrides=$false; batchSize=2000; startIndex=0; refreshView=$true } 240 600 -Force } catch {}
try { $null = Call-Mcp $Port 'set_category_visibility' @{ viewId=$vid; categoryIds=@(-2001320,-2001330); visible=$true } 60 120 -Force } catch {}
try {
  $lov = Payload (Call-Mcp $Port 'list_open_views' @{} 60 120 -Force)
  $base = @($lov.views) | Where-Object { $_.name -eq $BaseViewName } | Select-Object -First 1
  if($base){ $null = Call-Mcp $Port 'sync_view_state' @{ srcViewId=[int]$base.viewId; dstViewId=[int]$vid } 120 240 -Force }
} catch {}

# Ensure revision id
$rev = 0; try { $lr = Payload (Call-Mcp $Port 'list_revisions' @{} 60 120 -Force); $items=@(); try { $items=@($lr.revisions) } catch {}; if($items.Count -gt 0){ $rev=[int]$items[-1].id } } catch {}
if($rev -le 0){ try { $cr = Payload (Call-Mcp $Port 'create_default_revision' @{} 60 120 -Force); $rev = [int]$cr.revisionId } catch {} }

# Retry per element with escalating fallbacks
$log = @()
foreach($eid in $ids){
  $ok=$false; $stage='start'; $note='';
  try {
    $stage='projection';
    $pr = @{ viewId=$vid; elementId=[int]$eid; paddingMm=$PaddingMm; preZoom='element'; restoreZoom=$false; focusMarginMm=150; mode='aabb'; revisionId=$rev }
    $res = Payload (Call-Mcp $Port 'create_revision_cloud_for_element_projection' $pr 480 1200 -Force)
    $cid=0; try { $cid=[int]$res.cloudId } catch { $cid=0 }
    if($cid -gt 0){ $ok=$true; $note='projection'; }
  } catch { $note = 'projection-ex' }
  if(-not $ok){
    try {
      $stage='rect1';
      # centroid
      $einfo = Payload (Call-Mcp $Port 'get_element_info' @{ elementIds=@([int]$eid); rich=$true } 180 600 -Force)
      $e = @($einfo.elements)[0]; $cx=0.0; $cy=0.0
      if($e -and $e.bboxMm){ $cx = 0.5*([double]$e.bboxMm.min.x + [double]$e.bboxMm.max.x); $cy = 0.5*([double]$e.bboxMm.min.y + [double]$e.bboxMm.max.y) }
      elseif($e -and $e.coordinatesMm){ $cx=[double]$e.coordinatesMm.x; $cy=[double]$e.coordinatesMm.y }
      $loop = @(
        @{ start=@{x=($cx-$RectW1/2); y=($cy-$RectH1/2); z=0}; end=@{x=($cx+$RectW1/2); y=($cy-$RectH1/2); z=0} },
        @{ start=@{x=($cx+$RectW1/2); y=($cy-$RectH1/2); z=0}; end=@{x=($cx+$RectW1/2); y=($cy+$RectH1/2); z=0} },
        @{ start=@{x=($cx+$RectW1/2); y=($cy+$RectH1/2); z=0}; end=@{x=($cx-$RectW1/2); y=($cy+$RectH1/2); z=0} },
        @{ start=@{x=($cx-$RectW1/2); y=($cy+$RectH1/2); z=0}; end=@{x=($cx-$RectW1/2); y=($cy-$RectH1/2); z=0} }
      )
      $res2 = Payload (Call-Mcp $Port 'create_revision_cloud' @{ viewId=$vid; revisionId=$rev; curveLoops=@($loop) } 180 600 -Force)
      $cid2=0; try { $cid2=[int]$res2.cloudId } catch { $cid2=0 }
      if($cid2 -gt 0){ $ok=$true; $note='rect1' }
    } catch { $note='rect1-ex' }
  }
  if(-not $ok){
    try {
      $stage='rect2';
      $einfo = Payload (Call-Mcp $Port 'get_element_info' @{ elementIds=@([int]$eid); rich=$true } 180 600 -Force)
      $e = @($einfo.elements)[0]; $cx=0.0; $cy=0.0
      if($e -and $e.bboxMm){ $cx = 0.5*([double]$e.bboxMm.min.x + [double]$e.bboxMm.max.x); $cy = 0.5*([double]$e.bboxMm.min.y + [double]$e.bboxMm.max.y) }
      elseif($e -and $e.coordinatesMm){ $cx=[double]$e.coordinatesMm.x; $cy=[double]$e.coordinatesMm.y }
      $loop = @(
        @{ start=@{x=($cx-$RectW2/2); y=($cy-$RectH2/2); z=0}; end=@{x=($cx+$RectW2/2); y=($cy-$RectH2/2); z=0} },
        @{ start=@{x=($cx+$RectW2/2); y=($cy-$RectH2/2); z=0}; end=@{x=($cx+$RectW2/2); y=($cy+$RectH2/2); z=0} },
        @{ start=@{x=($cx+$RectW2/2); y=($cy+$RectH2/2); z=0}; end=@{x=($cx-$RectW2/2); y=($cy+$RectH2/2); z=0} },
        @{ start=@{x=($cx-$RectW2/2); y=($cy+$RectH2/2); z=0}; end=@{x=($cx-$RectW2/2); y=($cy-$RectH2/2); z=0} }
      )
      $res3 = Payload (Call-Mcp $Port 'create_revision_cloud' @{ viewId=$vid; revisionId=$rev; curveLoops=@($loop) } 180 600 -Force)
      $cid3=0; try { $cid3=[int]$res3.cloudId } catch { $cid3=0 }
      if($cid3 -gt 0){ $ok=$true; $note='rect2' }
    } catch { $note='rect2-ex' }
  }
  $log += [pscustomobject]@{ elementId=[int]$eid; ok=$ok; note=$note }
}

$out = Join-Path $ROOT ("Work/retry_clouds_"+(Get-Date -Format 'yyyyMMdd_HHmmss')+".csv")
$log | Export-Csv -LiteralPath $out -NoTypeInformation -Encoding UTF8
Write-Host ("Saved log: " + $out) -ForegroundColor Green
