# @feature: rebuild walls in current view | keywords: 壁, ビュー, レベル
$ErrorActionPreference = 'Stop'

param(
  [int]$Port = 5210,
  [int]$JobTimeoutSec = 300,
  [switch]$DryRun
)

function Send-Queued {
  param([string]$Method, [hashtable]$Params, [int]$TimeoutSec = 600)
  $id = [DateTimeOffset]::Now.ToUnixTimeMilliseconds().ToString()
  $body = @{ jsonrpc='2.0'; method=$Method; params=$Params; id=$id } | ConvertTo-Json -Depth 100 -Compress
  $url = "http://127.0.0.1:$Port/enqueue?timeout=$JobTimeoutSec"
  $resp = Invoke-WebRequest -UseBasicParsing -ContentType 'application/json' -Method Post -Body $body $url | Select-Object -ExpandProperty Content | ConvertFrom-Json
  if (-not $resp.ok) { throw "enqueue failed: $($resp | ConvertTo-Json -Depth 10)" }
  $jobId = $resp.jobId
  $deadline = (Get-Date).AddSeconds($TimeoutSec)
  while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 400
    $jr = (Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:$Port/job/$jobId").Content | ConvertFrom-Json
    if ($jr.state -eq 'SUCCEEDED') {
      if ($jr.result_json) {
        try { $rj = $jr.result_json | ConvertFrom-Json } catch { $rj = $null }
        if ($rj -and $rj.result) { return $rj.result } elseif ($rj) { return $rj }
      }
      return $jr
    }
    if ($jr.state -in @('FAILED','TIMEOUT','DEAD')) { throw ("job failed: " + ($jr | ConvertTo-Json -Depth 10)) }
  }
  throw "timeout waiting result for $Method"
}

Write-Host "[1/6] get_current_view" -ForegroundColor Cyan
$view = Send-Queued -Method 'get_current_view' -Params @{} -TimeoutSec 900
$viewId = [int]$view.viewId
Write-Host "viewId=$viewId"

Write-Host "[2/6] get_elements_in_view(viewId=$viewId)" -ForegroundColor Cyan
$rows = (Send-Queued -Method 'get_elements_in_view' -Params @{ viewId=$viewId } -TimeoutSec 900).rows
$idsInView = New-Object 'System.Collections.Generic.HashSet[int]'
foreach ($r in $rows) { [void]$idsInView.Add([int]$r.elementId) }
Write-Host ("elements in view = " + $idsInView.Count)

Write-Host "[3/6] get_walls (entire doc)" -ForegroundColor Cyan
$gw = Send-Queued -Method 'get_walls' -Params @{} -TimeoutSec 900
$walls = @(); foreach ($w in $gw.walls) { $walls += $w }
$target = $walls | Where-Object { $idsInView.Contains([int]$_.elementId) }
if ($target.Count -eq 0) { Write-Host "No walls in current view." -ForegroundColor Yellow; exit 0 }
Write-Host ("walls in view = " + $target.Count)

Write-Host "[4/6] get_element_info(rich) for selected walls" -ForegroundColor Cyan
$ids = @(); foreach ($w in $target) { $ids += [int]$w.elementId }
$info = Send-Queued -Method 'get_element_info' -Params @{ elementIds = $ids; rich = $true } -TimeoutSec 900
$consById = @{}
foreach ($e in $info.elements) { $consById[[int]$e.elementId] = $e.constraints }

if ($DryRun) {
  Write-Host "[DRY-RUN] Skipping delete/create." -ForegroundColor Yellow
  $preview = [pscustomobject]@{ ok=$true; viewId=$viewId; wallsInView=$target.Count; ids=$ids }
  $preview | ConvertTo-Json -Depth 10
  exit 0
}

Write-Host "[5/6] delete existing walls in view" -ForegroundColor Cyan
$deleted = New-Object System.Collections.Generic.List[int]
foreach ($w in $target) {
  $eid = [int]$w.elementId
  $resDel = Send-Queued -Method 'delete_wall' -Params @{ elementId = $eid } -TimeoutSec 900
  if ($resDel.ok -ne $true) { throw "delete failed for $eid" }
  [void]$deleted.Add($eid)
}
Write-Host ("deleted count = " + $deleted.Count)

Write-Host "[6/6] recreate walls at same baseline/type/levels" -ForegroundColor Cyan
$created = New-Object System.Collections.Generic.List[int]
foreach ($w in $target) {
  $eid = [int]$w.elementId
  $cons = $consById[$eid]
  $p = @{}
  $p.start = @{ x = $w.start.x; y = $w.start.y; z = $w.start.z }
  $p.end   = @{ x = $w.end.x;   y = $w.end.y;   z = $w.end.z }
  if ($w.typeId) { $p.wallTypeId = [int]$w.typeId }
  if ($w.levelId) { $p.baseLevelId = [int]$w.levelId }
  if ($cons -and $cons.wall) {
    if ($cons.wall.baseLevelId) { $p.baseLevelId = [int]$cons.wall.baseLevelId }
    if ($cons.wall.baseOffsetMm) { $p.baseOffsetMm = [double]$cons.wall.baseOffsetMm }
    if ($cons.wall.topLevelId) {
      $p.topLevelId = [int]$cons.wall.topLevelId
      if ($cons.wall.topOffsetMm) { $p.topOffsetMm = [double]$cons.wall.topOffsetMm }
    } elseif ($w.height) {
      $p.heightMm = [double]$w.height
    }
  } elseif ($w.height) {
    $p.heightMm = [double]$w.height
  }
  $resC = Send-Queued -Method 'create_wall' -Params $p -TimeoutSec 900
  if ($resC.ok -ne $true) { throw "create failed for original $eid" }
  [void]$created.Add([int]$resC.elementId)
}
Write-Host ("created count = " + $created.Count)

$summary = [pscustomobject]@{
  ok = $true
  viewId = $viewId
  wallsInView = $target.Count
  deleted = $deleted
  created = $created
}
$summary | ConvertTo-Json -Depth 10

