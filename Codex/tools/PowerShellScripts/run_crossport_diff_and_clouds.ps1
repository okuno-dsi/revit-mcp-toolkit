# @feature: run crossport diff and clouds | keywords: スペース, ビュー, スナップショット
param(
  [int]$LeftPort = 5210,
  [int]$RightPort = 5211,
  [switch]$CleanOldClouds,
  [int]$WaitSec = 300,
  [int]$JobTimeoutSec = 900,
  [double]$PaddingMm = 150.0,
  [double]$PosTolMm = 600.0,
  [double]$LenTolMm = 150.0,
  [string]$BaseViewName = 'RSL1'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8='1'
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

$SCRIPT_DIR = $PSScriptRoot
$ROOT = (Resolve-Path (Join-Path $SCRIPT_DIR '..\\..')).Path
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'

function Call-Mcp { param([int]$Port,[string]$Method,[hashtable]$Params,[int]$W=[int]$WaitSec,[int]$T=[int]$JobTimeoutSec,[switch]$Force)
  $pjson = ($Params | ConvertTo-Json -Depth 100 -Compress)
  $args = @('--port',$Port,'--command',$Method,'--params',$pjson,'--wait-seconds',[string]$W)
  if($T -gt 0){ $args += @('--timeout-sec',[string]$T) }
  if($Force){ $args += '--force' }
  $tmp = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ("mcp_"+[System.IO.Path]::GetRandomFileName()+".json"))
  $args += @('--output-file',$tmp)
  $null = & python -X utf8 $PY @args 2>$null
  $code=$LASTEXITCODE
  $txt=''; try{ $txt = Get-Content -LiteralPath $tmp -Raw -Encoding UTF8 } catch{}
  if(Test-Path $tmp){ Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
  if($code -ne 0){ throw "MCP failed ($Method): $txt" }
  if([string]::IsNullOrWhiteSpace($txt)){ throw "Empty MCP response ($Method)" }
  return ($txt | ConvertFrom-Json -Depth 400)
}

function Get-Payload($obj){ if($obj.result -and $obj.result.result){ return $obj.result.result } elseif($obj.result){ return $obj.result } else { return $obj } }

function Activate-BaseView { param([int]$Port,[string]$Name)
  try {
    $ov = Call-Mcp $Port 'open_views' @{ names = @($Name) } 60 120 -Force | Get-Payload
  } catch {}
  try {
    $lov = Call-Mcp $Port 'list_open_views' @{} 60 120 -Force | Get-Payload
    $vs = @($lov.views)
    $target = $vs | Where-Object { ([string]$_.name) -eq $Name } | Select-Object -First 1
    if($target){ $null = Call-Mcp $Port 'activate_view' @{ viewId = [int]$target.viewId } 60 120 -Force }
  } catch {}
}

function Duplicate-View-WithSuffix { param([int]$Port)
  $cv = Get-Payload (Call-Mcp $Port 'get_current_view' @{} 60 120 -Force)
  $vid = [int]$cv.viewId
  $vi = $null; try { $vi = Get-Payload (Call-Mcp $Port 'get_view_info' @{ viewId=$vid } 60 120 -Force) } catch {}
  $base = ''
  try { if($vi -and $vi.PSObject.Properties.Match('viewName').Count -gt 0){ $base = [string]$vi.viewName } } catch {}
  if([string]::IsNullOrWhiteSpace($base)){
    try { if($vi -and $vi.PSObject.Properties.Match('name').Count -gt 0){ $base = [string]$vi.name } } catch {}
  }
  if([string]::IsNullOrWhiteSpace($base)){ $base = "View_"+$vid }
  $desired = ($base + ' 相違')
  $idem = ("dup:{0}:{1}" -f $vid, $desired)
  $dup = Get-Payload (Call-Mcp $Port 'duplicate_view' @{ viewId=$vid; withDetailing=$true; desiredName=$desired; onNameConflict='returnExisting'; idempotencyKey=$idem } 180 360 -Force)
  $newVid = 0; try { $newVid = [int]$dup.viewId } catch {} ; if($newVid -le 0){ try { $newVid = [int]$dup.newViewId } catch {} }
  if($newVid -le 0){ throw "duplicate_view did not return viewId (port=$Port)" }
  # clear template
  try { $null = Call-Mcp $Port 'set_view_template' @{ viewId=$newVid; clear=$true } 60 120 -Force } catch {}
  try { $null = Call-Mcp $Port 'activate_view' @{ viewId=$newVid } 30 60 -Force } catch {}
  return @{ viewId=$newVid; name=$desired }
}

# 1) Snapshots (delete old and create new)
Write-Host "Creating snapshots for $LeftPort and $RightPort" -ForegroundColor Cyan
# Ensure same base view name is active on both ports before snapshot
Activate-BaseView -Port $LeftPort -Name $BaseViewName
Activate-BaseView -Port $RightPort -Name $BaseViewName
& pwsh -NoProfile -File (Join-Path $SCRIPT_DIR 'create_structural_details_snapshot.ps1') -Port $LeftPort -DeleteOld | Tee-Object -FilePath (Join-Path $ROOT 'Projects\\snap_left.log') | Out-Null
& pwsh -NoProfile -File (Join-Path $SCRIPT_DIR 'create_structural_details_snapshot.ps1') -Port $RightPort -DeleteOld | Tee-Object -FilePath (Join-Path $ROOT 'Projects\\snap_right.log') | Out-Null

function Get-LatestSnapshot([int]$Port){
  $pattern = "structural_details_port{0}_*.json" -f $Port
  $f = Get-ChildItem -Recurse -File -Filter $pattern (Join-Path $ROOT 'Work') | Sort-Object LastWriteTime | Select-Object -Last 1
  if(-not $f){ throw "Snapshot not found for port $Port" }
  return $f.FullName
}

$leftSnap = Get-LatestSnapshot -Port $LeftPort
$rightSnap = Get-LatestSnapshot -Port $RightPort
Write-Host "Left=$leftSnap`nRight=$rightSnap" -ForegroundColor DarkCyan

# 2) Strict diff
$ts = Get-Date -Format 'yyyyMMdd_HHmmss'
$csv = Join-Path $ROOT ("Projects/crossport_differences_"+$ts+".csv")
$leftIds = Join-Path $ROOT ("Projects/crossport_left_ids_"+$ts+".json")
$rightIds = Join-Path $ROOT ("Projects/crossport_right_ids_"+$ts+".json")
$pairs = Join-Path $ROOT ("Projects/crossport_modified_pairs_"+$ts+".json")
python -X utf8 (Join-Path $SCRIPT_DIR 'strict_crossport_diff.py') "$leftSnap" "$rightSnap" --csv "$csv" --left-ids "$leftIds" --right-ids "$rightIds" --pairs-out "$pairs" --pos-tol-mm $PosTolMm --len-tol-mm $LenTolMm --keys 'familyName,typeName,符号,H,B,tw,tf' | Tee-Object -FilePath (Join-Path $ROOT 'Projects\\crossport_diff_result.json') | Out-Null
Write-Host "Strict diff done: $csv" -ForegroundColor Green

if($CleanOldClouds){
  Write-Host "Cleaning old clouds in views like '*相違*'" -ForegroundColor Yellow
  & pwsh -NoProfile -File (Join-Path $SCRIPT_DIR 'delete_revision_clouds_by_viewname.ps1') -Port $LeftPort -NameLike '*相違*' -IncludeActiveView | Tee-Object -FilePath (Join-Path $ROOT 'Projects\\clean_left.json') | Out-Null
  & pwsh -NoProfile -File (Join-Path $SCRIPT_DIR 'delete_revision_clouds_by_viewname.ps1') -Port $RightPort -NameLike '*相違*' -IncludeActiveView | Tee-Object -FilePath (Join-Path $ROOT 'Projects\\clean_right.json') | Out-Null
}

# 3) Duplicate views and detach template
$Lview = Duplicate-View-WithSuffix -Port $LeftPort
$Rview = Duplicate-View-WithSuffix -Port $RightPort
Write-Host ("Left new viewId={0} | Right new viewId={1}" -f $Lview.viewId, $Rview.viewId) -ForegroundColor Cyan

# 4) Create clouds on unmatched/modified elements (batch)
function Read-Ids($path){ try { return @(Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json) } catch { return @() } }
$pairsObj = @(); try { $pairsObj = Get-Content -LiteralPath $pairs -Raw -Encoding UTF8 | ConvertFrom-Json } catch {}
# Only structural framing (-2001320) pairs
$SF = -2001320
$modLeft = @($pairsObj | Where-Object { try { [int]$_.leftCatId -eq $SF } catch { $false } } | ForEach-Object { [int]$_.leftId })
$modRight = @($pairsObj | Where-Object { try { [int]$_.rightCatId -eq $SF } catch { $false } } | ForEach-Object { [int]$_.rightId })
$Lids = $modLeft
$Rids = $modRight

if($Lids.Count -gt 0){
  # Clean clouds strictly in target view on left
  try {
    $lr = Call-Mcp $LeftPort 'list_revisions' @{ includeClouds=$true; cloudFields=@('elementId','viewId') } 60 240 -Force | Get-Payload
    $clouds = @(); foreach($rv in $lr.revisions){ if($rv.clouds){ $clouds += $rv.clouds } }
    $toDel = @($clouds | Where-Object { try { [int]$_.viewId -eq [int]$Lview.viewId } catch { $false } })
    foreach($c in $toDel){ try { $null = Call-Mcp $LeftPort 'delete_revision_cloud' @{ elementId = [int]$c.elementId } 60 240 -Force } catch {} }
  } catch {}
  # Per-element to attach comment
  $created = @()
  foreach($pair in $pairsObj){
    try {
      if([int]$pair.leftCatId -ne $SF){ continue }
      $eid = [int]$pair.leftId
      if($Lids -notcontains $eid){ continue }
      $res = Get-Payload (Call-Mcp $LeftPort 'create_revision_cloud_for_element_projection' @{ viewId=[int]$Lview.viewId; elementId=$eid; paddingMm=$PaddingMm; preZoom='element'; restoreZoom=$false; focusMarginMm=150; mode='aabb' } $WaitSec $JobTimeoutSec -Force)
      $cid = 0; try { $cid = [int]$res.cloudId } catch {}
      if($cid -le 0){ continue }
      $created += $cid
      # Build comment
      $diffs = $pair.diffs | ForEach-Object { "{0}: {1} -> {2}" -f $_.key,$_.left,$_.right }
      $comment = '差分: ' + ($diffs -join '; ')
      try { $null = Call-Mcp $LeftPort 'set_revision_cloud_parameter' @{ elementId=$cid; paramName='Comments'; value=$comment } 60 120 -Force } catch { try { $null = Call-Mcp $LeftPort 'set_revision_cloud_parameter' @{ elementId=$cid; paramName='コメント'; value=$comment } 60 120 -Force } catch {} }
    } catch {}
  }
  @{ ok = $true; created = $created.Count } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ROOT 'Projects\\clouds_left_result.json') -Encoding UTF8
}
if($Rids.Count -gt 0){
  # Clean clouds strictly in target view on right
  try {
    $rr = Call-Mcp $RightPort 'list_revisions' @{ includeClouds=$true; cloudFields=@('elementId','viewId') } 60 240 -Force | Get-Payload
    $clouds2 = @(); foreach($rv in $rr.revisions){ if($rv.clouds){ $clouds2 += $rv.clouds } }
    $toDel2 = @($clouds2 | Where-Object { try { [int]$_.viewId -eq [int]$Rview.viewId } catch { $false } })
    foreach($c in $toDel2){ try { $null = Call-Mcp $RightPort 'delete_revision_cloud' @{ elementId = [int]$c.elementId } 60 240 -Force } catch {} }
  } catch {}
  $created = @()
  foreach($pair in $pairsObj){
    try {
      if([int]$pair.rightCatId -ne $SF){ continue }
      $eid = [int]$pair.rightId
      if($Rids -notcontains $eid){ continue }
      $res = Get-Payload (Call-Mcp $RightPort 'create_revision_cloud_for_element_projection' @{ viewId=[int]$Rview.viewId; elementId=$eid; paddingMm=$PaddingMm; preZoom='element'; restoreZoom=$false; focusMarginMm=150; mode='aabb' } $WaitSec $JobTimeoutSec -Force)
      $cid = 0; try { $cid = [int]$res.cloudId } catch {}
      if($cid -le 0){ continue }
      $created += $cid
      $diffs = $pair.diffs | ForEach-Object { "{0}: {1} -> {2}" -f $_.key,$_.left,$_.right }
      $comment = '差分: ' + ($diffs -join '; ')
      try { $null = Call-Mcp $RightPort 'set_revision_cloud_parameter' @{ elementId=$cid; paramName='Comments'; value=$comment } 60 120 -Force } catch { try { $null = Call-Mcp $RightPort 'set_revision_cloud_parameter' @{ elementId=$cid; paramName='コメント'; value=$comment } 60 120 -Force } catch {} }
    } catch {}
  }
  @{ ok = $true; created = $created.Count } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ROOT 'Projects\\clouds_right_result.json') -Encoding UTF8
}

Write-Host "Completed: CSV=$csv | LeftView=$($Lview.viewId) | RightView=$($Rview.viewId)" -ForegroundColor Green






