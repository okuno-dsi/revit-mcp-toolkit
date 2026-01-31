# @feature: cloud from ids csv | keywords: スペース, ビュー
param(
  [int]$LeftPort = 5210,
  [int]$RightPort = 5211,
  [string]$BaseViewName = 'RSL1',
  [string]$CsvPath,
  [double]$PaddingMm = 150.0
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
  $pjson = ($Params | ConvertTo-Json -Depth 100 -Compress)
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

function Resolve-CompareViewId([int]$Port){
  $Target = ($BaseViewName + ' 相違')
  try {
    $lov = Payload (Call-Mcp $Port 'list_open_views' @{} 60 120 -Force)
    $views = @($lov.views)
    $v = $views | Where-Object { $_.name -eq $Target } | Select-Object -First 1
    if(-not $v){ $v = $views | Where-Object { $_.name -like ($Target+'*') } | Select-Object -First 1 }
    if($v){ try { $vid=[int]$v.viewId; $null = Call-Mcp $Port 'activate_view' @{ viewId=$vid } 60 120 -Force; return $vid } catch {} }
  } catch {}
  # Try to open by name
  try { $null = Call-Mcp $Port 'open_views' @{ names=@($Target) } 60 120 -Force } catch {}
  try {
    $lov2 = Payload (Call-Mcp $Port 'list_open_views' @{} 60 120 -Force)
    $v2 = @($lov2.views) | Where-Object { $_.name -eq $Target } | Select-Object -First 1
    if($v2){ $vid=[int]$v2.viewId; $null = Call-Mcp $Port 'activate_view' @{ viewId=$vid } 60 120 -Force; return $vid }
  } catch {}
  # Duplicate from base
  try {
    $lov3 = Payload (Call-Mcp $Port 'list_open_views' @{} 60 120 -Force)
    $base = @($lov3.views) | Where-Object { $_.name -eq $BaseViewName } | Select-Object -First 1
    if($base){
      $dup = Payload (Call-Mcp $Port 'duplicate_view' @{ viewId=[int]$base.viewId; withDetailing=$true; desiredName=$Target; onNameConflict='returnExisting'; idempotencyKey=("dup:{0}:{1}" -f $base.viewId,$Target) } 180 360 -Force)
      $vid=0; try { $vid=[int]$dup.viewId } catch { try { $vid=[int]$dup.newViewId } catch { $vid=0 } }
      if($vid -gt 0){ $null = Call-Mcp $Port 'activate_view' @{ viewId=$vid } 60 120 -Force; return $vid }
    }
  } catch {}
  throw "Port ${Port}: 相違ビューが見つかりません（'$Target'）"
}

function Ensure-RevisionId([int]$Port){
  try { $lr = Payload (Call-Mcp $Port 'list_revisions' @{ } 60 120 -Force); $items = @(); try { $items = @($lr.revisions) } catch {}; if($items.Count -gt 0){ try { return [int]$items[-1].id } catch {} } } catch {}
  try { $cr = Payload (Call-Mcp $Port 'create_default_revision' @{} 60 120 -Force); $rid = 0; try { $rid = [int]$cr.revisionId } catch {}; if($rid -gt 0){ return $rid } } catch {}
  return 0
}

function Get-CentroidMmById([int]$Port,[int]$ElemId){
  $info = Payload (Call-Mcp $Port 'get_element_info' @{ elementIds=@($ElemId); rich=$true } 120 480 -Force)
  $e = @($info.elements)[0]; if(-not $e){ return @{ x=0.0; y=0.0; z=0.0 } }
  if($e.bboxMm){
    try{ return @{ x = 0.5*([double]$e.bboxMm.min.x + [double]$e.bboxMm.max.x); y = 0.5*([double]$e.bboxMm.min.y + [double]$e.bboxMm.max.y); z = 0.5*([double]$e.bboxMm.min.z + [double]$e.bboxMm.max.z) } }catch{}
  }
  if($e.coordinatesMm){ return @{ x=[double]$e.coordinatesMm.x; y=[double]$e.coordinatesMm.y; z=[double]$e.coordinatesMm.z } }
  return @{ x=0.0; y=0.0; z=0.0 }
}

function Prefetch-Centroids([int]$Port,[int[]]$Ids){
  $map = @{}
  if(-not $Ids -or $Ids.Count -eq 0){ return $map }
  $chunk = 200
  for($i=0; $i -lt $Ids.Count; $i+=$chunk){
    $batch = @($Ids[$i..([Math]::Min($i+$chunk-1,$Ids.Count-1))])
    try {
      $res = Payload (Call-Mcp $Port 'get_element_info' @{ elementIds=$batch; rich=$true } 240 900 -Force)
      $els = @(); try { $els = @($res.elements) } catch {}
      foreach($e in $els){
        try{
          $id = [int]$e.elementId
          $cx = 0.0; $cy = 0.0; $cz = 0.0
          if($e.bboxMm){ $cx = 0.5*([double]$e.bboxMm.min.x + [double]$e.bboxMm.max.x); $cy = 0.5*([double]$e.bboxMm.min.y + [double]$e.bboxMm.max.y); $cz = 0.5*([double]$e.bboxMm.min.z + [double]$e.bboxMm.max.z) }
          elseif($e.coordinatesMm){ $cx = [double]$e.coordinatesMm.x; $cy = [double]$e.coordinatesMm.y; $cz = [double]$e.coordinatesMm.z }
          $map[[string]$id] = @{ x=$cx; y=$cy; z=$cz }
        } catch {}
      }
    } catch {}
  }
  return $map
}

function Get-VisibleIdSet([int]$Port,[int]$ViewId){
  $set = New-Object System.Collections.Generic.HashSet[int]
  try {
    $shape = @{ idsOnly = $true; page = @{ limit = 200000 } }
    $res = Payload (Call-Mcp $Port 'get_elements_in_view' @{ viewId=$ViewId; _shape=$shape } 180 480 -Force)
    $ids = @()
    foreach($path in 'result.result.elementIds','result.elementIds','elementIds'){
      try{ $cur=$res; foreach($seg in $path.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $ids=@($cur); break }catch{}
    }
    foreach($v in $ids){ try { [void]$set.Add([int]$v) } catch {} }
  } catch {}
  return $set
}

function Create-RectCloudAt([int]$Port,[int]$ViewId,[double]$cx,[double]$cy,[double]$w,[double]$h,[int]$RevId){
  $x0 = $cx - ($w/2.0); $x1 = $cx + ($w/2.0)
  $y0 = $cy - ($h/2.0); $y1 = $cy + ($h/2.0)
  $loop = @(
    @{ start=@{x=$x0;y=$y0;z=0}; end=@{x=$x1;y=$y0;z=0} },
    @{ start=@{x=$x1;y=$y0;z=0}; end=@{x=$x1;y=$y1;z=0} },
    @{ start=@{x=$x1;y=$y1;z=0}; end=@{x=$x0;y=$y1;z=0} },
    @{ start=@{x=$x0;y=$y1;z=0}; end=@{x=$x0;y=$y0;z=0} }
  )
  $pr = @{ viewId=$ViewId; curveLoops=@($loop) }
  if($RevId -gt 0){ $pr['revisionId'] = $RevId }
  try { return (Payload (Call-Mcp $Port 'create_revision_cloud' $pr 180 600 -Force)) } catch { return $null }
}

if(-not (Test-Path -LiteralPath $CsvPath)){ throw "CSV not found: $CsvPath" }
$rows = Import-Csv -LiteralPath $CsvPath -Encoding UTF8
$idsL = @([int[]](@($rows | Where-Object { try { [int]$_.'port' -eq $LeftPort } catch { $false } } | ForEach-Object { try { [int]$_.'elementId' } catch { $null } } ) | Where-Object { $_ -ne $null } | Sort-Object -Unique))
$idsR = @([int[]](@($rows | Where-Object { try { [int]$_.'port' -eq $RightPort } catch { $false } } | ForEach-Object { try { [int]$_.'elementId' } catch { $null } } ) | Where-Object { $_ -ne $null } | Sort-Object -Unique))

$Lview = Resolve-CompareViewId -Port $LeftPort
$Rview = Resolve-CompareViewId -Port $RightPort

# Visibility prep
try { $null = Call-Mcp $LeftPort  'set_view_template' @{ viewId=$Lview; clear=$true } 60 120 -Force } catch {}
try { $null = Call-Mcp $RightPort 'set_view_template' @{ viewId=$Rview; clear=$true } 60 120 -Force } catch {}
try { $null = Call-Mcp $LeftPort  'show_all_in_view' @{ viewId=$Lview; detachViewTemplate=$true; includeTempReset=$true; unhideElements=$true; clearElementOverrides=$false; batchSize=2000; startIndex=0; refreshView=$true } 180 480 -Force } catch {}
try { $null = Call-Mcp $RightPort 'show_all_in_view' @{ viewId=$Rview; detachViewTemplate=$true; includeTempReset=$true; unhideElements=$true; clearElementOverrides=$false; batchSize=2000; startIndex=0; refreshView=$true } 180 480 -Force } catch {}
try { $null = Call-Mcp $LeftPort  'set_category_visibility' @{ viewId=$Lview; categoryIds=@(-2001320,-2001330); visible=$true } 60 120 -Force } catch {}
try { $null = Call-Mcp $RightPort 'set_category_visibility' @{ viewId=$Rview; categoryIds=@(-2001320,-2001330); visible=$true } 60 120 -Force } catch {}
try {
  $baseL = (Payload (Call-Mcp $LeftPort 'list_open_views' @{} 60 120 -Force)).views | Where-Object { $_.name -eq $BaseViewName } | Select-Object -First 1
  if($baseL){ $null = Call-Mcp $LeftPort 'sync_view_state' @{ srcViewId=[int]$baseL.viewId; dstViewId=[int]$Lview } 120 240 -Force }
} catch {}
try {
  $baseR = (Payload (Call-Mcp $RightPort 'list_open_views' @{} 60 120 -Force)).views | Where-Object { $_.name -eq $BaseViewName } | Select-Object -First 1
  if($baseR){ $null = Call-Mcp $RightPort 'sync_view_state' @{ srcViewId=[int]$baseR.viewId; dstViewId=[int]$Rview } 120 240 -Force }
} catch {}

# Ensure revision ids
$revL = Ensure-RevisionId -Port $LeftPort
$revR = Ensure-RevisionId -Port $RightPort

function Cloud-Ids([int]$Port,[int]$ViewId,[int[]]$Ids,[int]$RevId){
  $ok=0; $fail=0
  foreach($eid in $Ids){
    $cid = 0
    try {
      $pr = @{ viewId=$ViewId; elementId=[int]$eid; paddingMm=$PaddingMm; preZoom='element'; restoreZoom=$false; focusMarginMm=150; mode='aabb' }
      if($RevId -gt 0){ $pr['revisionId'] = $RevId }
      $res = Payload (Call-Mcp $Port 'create_revision_cloud_for_element_projection' $pr 480 1200 -Force)
      try { $cid = [int]$res.cloudId } catch { $cid = 0 }
      if($cid -le 0){
        $pr.Remove('revisionId')
        $res2 = Payload (Call-Mcp $Port 'create_revision_cloud_for_element_projection' $pr 480 1200 -Force)
        try { $cid = [int]$res2.cloudId } catch { $cid = 0 }
      }
      if($cid -le 0){
        $c = Get-CentroidMmById -Port $Port -ElemId $eid
        $rect = Create-RectCloudAt -Port $Port -ViewId $ViewId -cx ([double]$c.x) -cy ([double]$c.y) -w 600 -h 450 -RevId $RevId
        try { $cid = [int]$rect.cloudId } catch { $cid = 0 }
      }
    } catch { $cid = 0 }
    if($cid -gt 0){ $ok++ } else { $fail++ }
  }
  return @{ ok=$ok; fail=$fail }
}

$visL = Get-VisibleIdSet -Port $LeftPort -ViewId $Lview
$visR = Get-VisibleIdSet -Port $RightPort -ViewId $Rview
$centL = Prefetch-Centroids -Port $LeftPort -Ids $idsL
$centR = Prefetch-Centroids -Port $RightPort -Ids $idsR

function Cloud-Ids([int]$Port,[int]$ViewId,[int[]]$Ids,[int]$RevId,[System.Collections.Generic.HashSet[int]]$VisibleSet,$Centroids){
  $ok=0; $fail=0
  foreach($eid in $Ids){
    $cid = 0
    try {
      $isVisible = $false; try { $isVisible = $VisibleSet.Contains([int]$eid) } catch { $isVisible = $false }
      if($isVisible){
        $pr = @{ viewId=$ViewId; elementId=[int]$eid; paddingMm=$PaddingMm }
        if($RevId -gt 0){ $pr['revisionId'] = $RevId }
        $res = Payload (Call-Mcp $Port 'create_revision_cloud_for_element_projection' $pr 360 900 -Force)
        try { $cid = [int]$res.cloudId } catch { $cid = 0 }
        if($cid -le 0){
          $pr.Remove('revisionId')
          $res2 = Payload (Call-Mcp $Port 'create_revision_cloud_for_element_projection' $pr 360 900 -Force)
          try { $cid = [int]$res2.cloudId } catch { $cid = 0 }
        }
      }
      if($cid -le 0){
        $c = $null; try { $c = $Centroids[[string][int]$eid] } catch { $c=$null }
        if(-not $c){ $c = Get-CentroidMmById -Port $Port -ElemId $eid }
        $rect = Create-RectCloudAt -Port $Port -ViewId $ViewId -cx ([double]$c.x) -cy ([double]$c.y) -w 600 -h 450 -RevId $RevId
        try { $cid = [int]$rect.cloudId } catch { $cid = 0 }
      }
    } catch { $cid = 0 }
    if($cid -gt 0){ $ok++ } else { $fail++ }
  }
  return @{ ok=$ok; fail=$fail }
}

$resL = Cloud-Ids -Port $LeftPort -ViewId $Lview -Ids $idsL -RevId $revL -VisibleSet $visL -Centroids $centL
$resR = Cloud-Ids -Port $RightPort -ViewId $Rview -Ids $idsR -RevId $revR -VisibleSet $visR -Centroids $centR

Write-Host ("Clouds created. Left ok="+$resL.ok+" fail="+$resL.fail+" | Right ok="+$resR.ok+" fail="+$resR.fail) -ForegroundColor Green
