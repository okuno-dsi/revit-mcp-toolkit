# @feature: create rooms from candidates durable | keywords: 壁, 部屋, レベル, スナップショット
param(
  [int]$Port = 5210,
  [string]$LevelName = '1FL',
  [string]$CandidatesFile = '..\Logs\roomless_candidates.json',
  [int]$MaxRooms = 50,
  [double]$PrecheckScale = 0.5,
  [switch]$PrecheckOnly,
  [int]$TimeoutSec = 90,
  [double]$WaitSeconds = 120,
  [switch]$UseClassify
)

chcp 65001 > $null
$ErrorActionPreference = 'Stop'

function Write-JsonNoBom {
  param([string]$Path, [string]$Json)
  $enc = New-Object System.Text.UTF8Encoding($false)
  [System.IO.File]::WriteAllText($Path, $Json, $enc)
}

function Distance2D([double]$x1,[double]$y1,[double]$x2,[double]$y2){ return [math]::Sqrt((($x1-$x2)*($x1-$x2)) + (($y1-$y2)*($y1-$y2))) }

function Get-RoomsOnLevel {
  param([int]$Port,[string]$LevelName)
  $script = Join-Path $PSScriptRoot 'send_revit_command_durable.py'
  $tmp = New-TemporaryFile
  try {
    Write-JsonNoBom -Path $tmp -Json '{"skip":0,"count":2000}'
    $out = & python $script --port $Port --command get_rooms --params-file $tmp --wait-seconds 60 2>&1
    try { $obj = $out | ConvertFrom-Json } catch { return @() }
    $res = $obj.result.result; if(-not $res){ $res = $obj.result }
    if($res -and $res.rooms){ return ($res.rooms | Where-Object { $_.level -eq $LevelName }) }
    return @()
  } finally { Remove-Item -ErrorAction SilentlyContinue $tmp }
}

function Invoke-ClassifyPointsInRoom {
  param([int]$RoomId, [array]$Points)
  $durable = Join-Path $PSScriptRoot 'send_revit_command_durable.py'
  $tmp = New-TemporaryFile
  try {
    $payload = @{ roomId = $RoomId; points = @() }
    foreach($pt in $Points){ $payload.points += @{ x=[double]$pt.x; y=[double]$pt.y; z=[double]$pt.z } }
    Write-JsonNoBom -Path $tmp -Json ($payload | ConvertTo-Json -Depth 8 -Compress)
    $out = & python $durable --port $Port --command classify_points_in_room --params-file $tmp --wait-seconds 60 --force 2>&1
    try { return ($out | ConvertFrom-Json) } catch { return @{ ok=$false; error='Non-JSON response'; raw=$out } }
  } finally { Remove-Item -ErrorAction SilentlyContinue $tmp }
}

function Test-PointCoveredByAnyRoom {
  param([array]$Rooms, [hashtable]$Point)
  if(-not $UseClassify){ return $false }
  foreach($rm in $Rooms){
    if(-not $rm.elementId){ continue }
    $resp = Invoke-ClassifyPointsInRoom -RoomId ([int]$rm.elementId) -Points @($Point)
    try {
      $obj = $resp; $res = $null
      if($obj.result -and $obj.result.result){ $res = $obj.result.result } elseif($obj.result){ $res = $obj.result } else { $res = $obj }
      # common shapes: {results:[{inside:true}]}
      $arr = @()
      if($res -and $res.results){ $arr = @($res.results) } elseif($res -is [System.Array]){ $arr = @($res) }
      foreach($r in $arr){ if($r -and ($r.inside -or $r.inRoom -or $r.contains)){ return $true } }
    } catch {}
  }
  return $false
}

Write-Host "[1/4] Load candidates" -ForegroundColor Cyan
if(-not (Test-Path $CandidatesFile)){ $CandidatesFile = Resolve-Path (Join-Path $PSScriptRoot '..\Logs\roomless_candidates.json') }
$cand = Get-Content $CandidatesFile -Raw | ConvertFrom-Json
$points = @($cand.points)
if(-not $points){ throw "No points found in $CandidatesFile" }

Write-Host "[2/4] Resolve levelId (optional)" -ForegroundColor Cyan
$levelId = 0
try {
  # Use send_revit_command_durable.py for reliability
  $py = Join-Path $PSScriptRoot 'send_revit_command_durable.py'
  $tmpParams = New-TemporaryFile
  Write-JsonNoBom -Path $tmpParams -Json '{"skip":0,"count":50}'
  $wallsJson = & python $py --port $Port --command get_walls --params-file $tmpParams --wait-seconds 60 2>&1
  $wobj = $wallsJson | ConvertFrom-Json
  $wres = $wobj.result.result; if(-not $wres){ $wres = $wobj.result }
  $wids = @($wres.walls | Select-Object -First 10 | ForEach-Object { $_.elementId })
  if($wids.Count -gt 0){
    $tmpParams2 = New-TemporaryFile
    Write-JsonNoBom -Path $tmpParams2 -Json ('{"elementIds":'+(ConvertTo-Json -InputObject $wids -Compress)+' ,"rich":true}')
    $infoJson = & python $py --port $Port --command get_element_info --params-file $tmpParams2 --wait-seconds 60 2>&1
    $iobj = $infoJson | ConvertFrom-Json
    $ires = $iobj.result.result; if(-not $ires){ $ires = $iobj.result }
    foreach($e in @($ires.elements)){
      if($e.constraints -and $e.constraints.wall -and $e.constraints.wall.baseLevelName -eq $LevelName){ $levelId = [int]$e.constraints.wall.baseLevelId; break }
    }
    Remove-Item -ErrorAction SilentlyContinue $tmpParams2
  }
  Remove-Item -ErrorAction SilentlyContinue $tmpParams
} catch {}
if($levelId -gt 0){ Write-Host ("  -> LevelId: {0}" -f $levelId) -ForegroundColor DarkCyan } else { Write-Host '  -> LevelId not resolved; will use levelName' -ForegroundColor DarkYellow }

Write-Host "[3/4] Pre-check occupancy & Create rooms (durable)" -ForegroundColor Cyan
$created=0; $failed=0; $skipped=0
$logDir = Resolve-Path (Join-Path $PSScriptRoot '..\Logs')
$logFile = Join-Path $logDir 'create_rooms_results_durable.jsonl'
if(Test-Path $logFile){ Remove-Item $logFile -Force }

# Initial rooms snapshot
$rooms = Get-RoomsOnLevel -Port $Port -LevelName $LevelName

$limit = [math]::Min($MaxRooms, $points.Count)
for($i=0; $i -lt $limit; $i++){
  $p = $points[$i]
  $radius = [math]::Min([double]$p.spanX, [double]$p.spanY) * [double]$PrecheckScale
  $occupied = $false
  if($UseClassify){ $occupied = Test-PointCoveredByAnyRoom -Rooms $rooms -Point @{ x=[double]$p.x; y=[double]$p.y; z=[double]$p.z } }
  if(-not $occupied){ foreach($r0 in $rooms){ if($r0.center){ if((Distance2D ([double]$p.x) ([double]$p.y) ([double]$r0.center.x) ([double]$r0.center.y)) -le $radius){ $occupied=$true; break } } } }
  if($occupied -or $PrecheckOnly){
    $skipped++
    $reason = if($PrecheckOnly){ 'PrecheckOnly' } else { 'Occupied' }
    @{ action='skip'; reason=$reason; point=$p; radius=$radius } | ConvertTo-Json -Depth 8 -Compress | Out-File -Append -FilePath $logFile -Encoding UTF8
    continue
  }

  $params = @{ __smoke_ok = $true }
  if($levelId -gt 0){ $params.levelId = $levelId } else { $params.levelName = $LevelName }
  # Prefer scalar x,y form
  $params.x = [double]$p.x; $params.y = [double]$p.y

  $pfile = New-TemporaryFile
  try{
    Write-JsonNoBom -Path $pfile -Json ($params | ConvertTo-Json -Depth 10 -Compress)
    $durable = Join-Path $PSScriptRoot 'send_revit_command_durable.py'
    $out = & python $durable --port $Port --command create_room --params-file $pfile --force --timeout-sec $TimeoutSec --wait-seconds $WaitSeconds 2>&1
    try { $obj = $out | ConvertFrom-Json } catch { $obj = @{ ok=$false; error="Non-JSON response"; raw=$out } }
    if($obj.ok -and ($obj.result.result.ok -or $obj.result.ok -or $obj.ok)){
      $created++
      # Update in-memory occupancy
      $rooms += [pscustomobject]@{ center = @{ x = [double]$p.x; y = [double]$p.y; z = [double]$p.z }; level = $LevelName }
    } else {
      $failed++
    }
    ($obj | ConvertTo-Json -Depth 12 -Compress) | Out-File -Append -FilePath $logFile -Encoding UTF8
  } finally{
    Remove-Item -ErrorAction SilentlyContinue $pfile
  }

  # Periodically refresh room list (in case names/centers change)
  if((($i+1) % 3) -eq 0){ $rooms = Get-RoomsOnLevel -Port $Port -LevelName $LevelName }
}

Write-Host "[4/4] Summary" -ForegroundColor Cyan
Write-Host ("  Created: {0}, Failed: {1}, Skipped: {2}" -f $created, $failed, $skipped) -ForegroundColor Green
Write-Host ("  Log: {0}" -f $logFile) -ForegroundColor DarkGreen



