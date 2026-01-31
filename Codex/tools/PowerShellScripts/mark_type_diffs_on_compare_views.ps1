# @feature: mark type diffs on compare views | keywords: スペース, ビュー, キャプチャ, スナップショット
param(
  [int]$LeftPort = 5210,
  [int]$RightPort = 5211,
  [string]$BaseViewName = 'RSL1',
  [double]$PosTolMm = 10.0,
  [double]$LenTolMm = 150.0,
  [double]$PaddingMm = 150.0,
  [double]$MinDistanceMm = 0.0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8='1'
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

$SCRIPT_DIR = $PSScriptRoot
$ROOT = (Resolve-Path (Join-Path $SCRIPT_DIR '..\\..')).Path
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'

function Call-Mcp { param([int]$Port,[string]$Method,[hashtable]$Params,[int]$W=180,[int]$T=600,[switch]$Force)
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

function Ensure-CompareView([int]$Port){
  $base = $BaseViewName; $desired = ($BaseViewName + ' 相違')
  # open base
  try { $null = Call-Mcp $Port 'open_views' @{ names=@($base) } 60 120 -Force } catch {}
  $lov = Payload (Call-Mcp $Port 'list_open_views' @{} 60 120 -Force)
  $views = @($lov.views)
  $b = $views | Where-Object { $_.name -eq $base } | Select-Object -First 1
  if(-not $b){ throw "Port ${Port}: ベースビュー '${base}' が見つかりません" }
  $dup = Payload (Call-Mcp $Port 'duplicate_view' @{ viewId=[int]$b.viewId; withDetailing=$true; desiredName=$desired; onNameConflict='returnExisting'; idempotencyKey=("dup:{0}:{1}" -f $b.viewId,$desired) } 180 360 -Force)
  $vid = 0; try { $vid = [int]$dup.viewId } catch {}
  if($vid -le 0){
    # try by name; if not exact, accept any '*相違*' pref
    $lov2 = Payload (Call-Mcp $Port 'list_open_views' @{} 60 120 -Force)
    $v = @($lov2.views) | Where-Object { $_.name -eq $desired } | Select-Object -First 1
    if(-not $v){ $v = @($lov2.views) | Where-Object { $_.name -like ($BaseViewName+' 相違*') } | Select-Object -First 1 }
    if($v){ $vid=[int]$v.viewId }
  }
  if($vid -le 0){
    # fallback: create timestamped
    $ts = Get-Date -Format 'HHmmss'; $alt = ($BaseViewName+' 相違 '+$ts)
    $dup2 = Payload (Call-Mcp $Port 'duplicate_view' @{ viewId=[int]$b.viewId; withDetailing=$true; desiredName=$alt; onNameConflict='increment'; idempotencyKey=("dup:{0}:{1}" -f $b.viewId,$alt) } 180 360 -Force)
    try { $vid = [int]$dup2.viewId } catch { $vid = 0 }
    if($vid -le 0){ $lov3 = Payload (Call-Mcp $Port 'list_open_views' @{} 60 120 -Force); $v3 = @($lov3.views) | Where-Object { $_.name -eq $alt } | Select-Object -First 1; if($v3){ $vid=[int]$v3.viewId } }
  }
  if($vid -le 0){ throw "Port ${Port}: 相違ビュー作成に失敗しました" }
  try { $null = Call-Mcp $Port 'set_view_template' @{ viewId=$vid; clear=$true } 60 120 -Force } catch {}
  try { $null = Call-Mcp $Port 'activate_view' @{ viewId=$vid } 60 120 -Force } catch {}
  return $vid
}

function Clean-CloudsInView([int]$Port,[int]$ViewId){
  try {
    $lr = Payload (Call-Mcp $Port 'list_revisions' @{ includeClouds=$true; cloudFields=@('elementId','viewId') } 60 240 -Force)
    $clouds = @(); foreach($rv in $lr.revisions){ if($rv.clouds){ $clouds += $rv.clouds } }
    $del = @($clouds | Where-Object { try { [int]$_.viewId -eq $ViewId } catch { $false } })
    foreach($c in $del){ try { $null = Call-Mcp $Port 'delete_revision_cloud' @{ elementId = [int]$c.elementId } 60 240 -Force } catch {} }
  } catch {}
}

Write-Host "Preparing compare views..." -ForegroundColor Cyan
$Lview = Ensure-CompareView -Port $LeftPort
$Rview = Ensure-CompareView -Port $RightPort

Clean-CloudsInView -Port $LeftPort -ViewId $Lview
Clean-CloudsInView -Port $RightPort -ViewId $Rview

# Ensure visibility and categories are sane on both compare views
try { $null = Call-Mcp $LeftPort  'show_all_in_view' @{ viewId=$Lview; detachViewTemplate=$true; includeTempReset=$true; unhideElements=$true; clearElementOverrides=$false; batchSize=2000; startIndex=0; refreshView=$true } 180 480 -Force } catch {}
try { $null = Call-Mcp $RightPort 'show_all_in_view' @{ viewId=$Rview; detachViewTemplate=$true; includeTempReset=$true; unhideElements=$true; clearElementOverrides=$false; batchSize=2000; startIndex=0; refreshView=$true } 180 480 -Force } catch {}
try { $null = Call-Mcp $LeftPort  'set_category_visibility' @{ viewId=$Lview; categoryIds=@(-2001320,-2001330); visible=$true } 60 120 -Force } catch {}
try { $null = Call-Mcp $RightPort 'set_category_visibility' @{ viewId=$Rview; categoryIds=@(-2001320,-2001330); visible=$true } 60 120 -Force } catch {}
try { $null = Call-Mcp $LeftPort  'view_fit' @{ viewId=$Lview } 30 60 -Force } catch {}
try { $null = Call-Mcp $RightPort 'view_fit' @{ viewId=$Rview } 30 60 -Force } catch {}

Write-Host "Computing type differences (pos tol $PosTolMm mm)..." -ForegroundColor Cyan
$leftSnap = (Get-ChildItem -Recurse -File -Filter ("structural_details_port{0}_*.json" -f $LeftPort) (Join-Path $ROOT 'Work') | Sort-Object LastWriteTime | Select-Object -Last 1).FullName
$rightSnap = (Get-ChildItem -Recurse -File -Filter ("structural_details_port{0}_*.json" -f $RightPort) (Join-Path $ROOT 'Work') | Sort-Object LastWriteTime | Select-Object -Last 1).FullName
if(-not $leftSnap -or -not $rightSnap){ throw 'Snapshots not found. Please run create_structural_details_snapshot.ps1 first.' }

$ts = Get-Date -Format 'yyyyMMdd_HHmmss'
$pairsPath = Join-Path $ROOT ("Work/crossport_modified_pairs_"+$ts+".json")
# Pair broadly to ensure type diffs are captured regardless of distance; filtering is applied below
python -X utf8 (Join-Path $SCRIPT_DIR 'strict_crossport_diff.py') "$leftSnap" "$rightSnap" --pairs-out "$pairsPath" --pos-tol-mm 100000 --len-tol-mm $LenTolMm --keys 'familyName,typeName,符号,H,B,tw,tf' | Out-Null

if(-not (Test-Path $pairsPath)){ throw 'pairs file not generated' }
$pairs = Get-Content -LiteralPath $pairsPath -Raw -Encoding UTF8 | ConvertFrom-Json

# Structural Framing only & typeName diffs only
$SF = -2001320
function Has-TypeDiff($pair){ foreach($d in @($pair.diffs)){ if([string]$d.key -eq 'typeName'){ return $true } } return $false }
$pFiltered = @($pairs | Where-Object { try { ([int]$_.leftCatId -eq $SF -or [int]$_.rightCatId -eq $SF) -and (Has-TypeDiff $_) } catch { $false } })

# Optionally compute distance and keep only those with distance >= $MinDistanceMm
function Get-CentroidMmById([int]$Port,[int]$ElemId){
  $info = Payload (Call-Mcp $Port 'get_element_info' @{ elementIds=@($ElemId); rich=$true } 120 480 -Force)
  $e = @($info.elements)[0]; if(-not $e){ return @{ x=0.0; y=0.0; z=0.0 } }
  if($e.bboxMm){
    try{ return @{ x = 0.5*([double]$e.bboxMm.min.x + [double]$e.bboxMm.max.x); y = 0.5*([double]$e.bboxMm.min.y + [double]$e.bboxMm.max.y); z = 0.5*([double]$e.bboxMm.min.z + [double]$e.bboxMm.max.z) } }catch{}
  }
  if($e.coordinatesMm){ return @{ x=[double]$e.coordinatesMm.x; y=[double]$e.coordinatesMm.y; z=[double]$e.coordinatesMm.z } }
  return @{ x=0.0; y=0.0; z=0.0 }
}

$pFiltered = @(
  foreach($pair in $pFiltered){
    try{
      $l=[int]$pair.leftId; $r=[int]$pair.rightId
      if($l -le 0 -and $r -le 0){ continue }
      $lc = if($l -gt 0){ Get-CentroidMmById -Port $LeftPort -ElemId $l } else { @{ x=0;y=0;z=0 } }
      $rc = if($r -gt 0){ Get-CentroidMmById -Port $RightPort -ElemId $r } else { @{ x=0;y=0;z=0 } }
      $dx = [double]$lc.x - [double]$rc.x; $dy = [double]$lc.y - [double]$rc.y; $dz = [double]$lc.z - [double]$rc.z
      $dist = [Math]::Sqrt($dx*$dx + $dy*$dy + $dz*$dz)
      if($dist -ge [double]$MinDistanceMm){ $pair }
    } catch {}
  }
)

Write-Host ("Pairs with typeName diff: " + $pFiltered.Count) -ForegroundColor Green

# Create per-element clouds with comment
$createdL = 0; $createdR = 0

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
foreach($pair in $pFiltered){
  try {
    $l = 0; $r = 0; try{ $l=[int]$pair.leftId }catch{}; try{ $r=[int]$pair.rightId }catch{}
    $diffs = @($pair.diffs) | ForEach-Object { "{0}: {1}->{2}" -f $_.key,$_.left,$_.right }
    $cmt = '差分: ' + ($diffs -join '; ')
    # Ensure a revision exists and use it when creating clouds
    $revL = 0; $revR = 0
    try {
      $lr = Payload (Call-Mcp $LeftPort 'list_revisions' @{ } 60 120 -Force)
      $items = @(); try { $items = @($lr.revisions) } catch {}
      if($items.Count -gt 0){ try { $revL = [int]$items[-1].id } catch {} }
      if($revL -le 0){ $cr = Payload (Call-Mcp $LeftPort 'create_default_revision' @{} 60 120 -Force); try { $revL = [int]$cr.revisionId } catch {} }
    } catch {}
    try {
      $rr = Payload (Call-Mcp $RightPort 'list_revisions' @{ } 60 120 -Force)
      $itemsR = @(); try { $itemsR = @($rr.revisions) } catch {}
      if($itemsR.Count -gt 0){ try { $revR = [int]$itemsR[-1].id } catch {} }
      if($revR -le 0){ $cr2 = Payload (Call-Mcp $RightPort 'create_default_revision' @{} 60 120 -Force); try { $revR = [int]$cr2.revisionId } catch {} }
    } catch {}
    if($l -gt 0){
      $prL = @{ viewId=$Lview; elementId=$l; paddingMm=$PaddingMm; preZoom='element'; restoreZoom=$false; focusMarginMm=150; mode='aabb' }
      if($revL -gt 0){ $prL['revisionId'] = $revL }
      $resL = $null
      try { $resL = Payload (Call-Mcp $LeftPort 'create_revision_cloud_for_element_projection' $prL 480 1200 -Force) } catch { $resL = $null }
      $cidL = 0; try { $cidL = [int]$resL.cloudId } catch {}
      if($cidL -le 0){
        # retry without revisionId
        $prL.Remove('revisionId')
        try { $resL = Payload (Call-Mcp $LeftPort 'create_revision_cloud_for_element_projection' $prL 480 1200 -Force); $cidL = 0; try { $cidL = [int]$resL.cloudId } catch {} } catch {}
        # fallback: small rectangle at element centroid in view coordinates
        if($cidL -le 0){
          $c = Get-CentroidMmById -Port $LeftPort -ElemId $l
          $rect = Create-RectCloudAt -Port $LeftPort -ViewId $Lview -cx ([double]$c.x) -cy ([double]$c.y) -w 400 -h 300 -RevId $revL
          try { $cidL = [int]$rect.cloudId } catch {}
        }
      }
      if($cidL -gt 0){ $createdL++; try { $null = Call-Mcp $LeftPort 'set_revision_cloud_parameter' @{ elementId=$cidL; paramName='Comments'; value=$cmt } 60 180 -Force } catch { try { $null = Call-Mcp $LeftPort 'set_revision_cloud_parameter' @{ elementId=$cidL; paramName='コメント'; value=$cmt } 60 180 -Force } catch {} } }
    }
    if($r -gt 0){
      $prR = @{ viewId=$Rview; elementId=$r; paddingMm=$PaddingMm; preZoom='element'; restoreZoom=$false; focusMarginMm=150; mode='aabb' }
      if($revR -gt 0){ $prR['revisionId'] = $revR }
      $resR = $null
      try { $resR = Payload (Call-Mcp $RightPort 'create_revision_cloud_for_element_projection' $prR 480 1200 -Force) } catch { $resR = $null }
      $cidR = 0; try { $cidR = [int]$resR.cloudId } catch {}
      if($cidR -le 0){
        $prR.Remove('revisionId')
        try { $resR = Payload (Call-Mcp $RightPort 'create_revision_cloud_for_element_projection' $prR 480 1200 -Force); $cidR = 0; try { $cidR = [int]$resR.cloudId } catch {} } catch {}
        if($cidR -le 0){
          $c2 = Get-CentroidMmById -Port $RightPort -ElemId $r
          $rect2 = Create-RectCloudAt -Port $RightPort -ViewId $Rview -cx ([double]$c2.x) -cy ([double]$c2.y) -w 400 -h 300 -RevId $revR
          try { $cidR = [int]$rect2.cloudId } catch {}
        }
      }
      if($cidR -gt 0){ $createdR++; try { $null = Call-Mcp $RightPort 'set_revision_cloud_parameter' @{ elementId=$cidR; paramName='Comments'; value=$cmt } 60 180 -Force } catch { try { $null = Call-Mcp $RightPort 'set_revision_cloud_parameter' @{ elementId=$cidR; paramName='コメント'; value=$cmt } 60 180 -Force } catch {} } }
    }
  } catch {}
}

# Save CSV summary with ports
$csv = Join-Path $ROOT ("Work/type_diffs_marked_"+$ts+".csv")
$rows = @()
foreach($pair in $pFiltered){
  $diffs = (@($pair.diffs | ForEach-Object { "{0}: {1}->{2}" -f $_.key,$_.left,$_.right })) -join '; '
  $rows += [pscustomobject]@{ port=$LeftPort;  elementId=[int]$pair.leftId;  categoryId=[int]$pair.leftCatId;  note=$diffs }
  $rows += [pscustomobject]@{ port=$RightPort; elementId=[int]$pair.rightId; categoryId=[int]$pair.rightCatId; note=$diffs }
}
$rows | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding UTF8

Write-Host ("Completed. LeftClouds="+$createdL+" RightClouds="+$createdR+" CSV="+$csv) -ForegroundColor Green
