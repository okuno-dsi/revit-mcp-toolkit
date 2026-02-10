# @feature: detect roomless enclosures | keywords: 壁, 部屋, レベル
param(
  [int]$Port = 5210,
  [string]$LevelName = '1FL',
  [int]$MinCellSizeMm = 1500,
  [int]$MaxCellSizeMm = 30000,
  [int]$OutputLimit = 50
)

chcp 65001 > $null
$ErrorActionPreference = 'Stop'

function Invoke-Mcp {
  param([string]$Method, [hashtable]$Params)
  $base = "http://localhost:$Port"
  $enqueue = "$base/enqueue"
  $get = "$base/get_result"
  $ts = [int64](([DateTimeOffset]::UtcNow).ToUnixTimeMilliseconds())
  if(-not $Params){ $Params = @{} }
  $payload = @{ jsonrpc='2.0'; method=$Method; params=$Params; id=$ts } | ConvertTo-Json -Depth 20 -Compress
  $headers = @{ Accept='application/json'; 'Accept-Charset'='utf-8' }
  Invoke-RestMethod -Method Post -Uri $enqueue -Body $payload -ContentType 'application/json; charset=utf-8' -Headers $headers | Out-Null
  do { Start-Sleep -Milliseconds 500; $r=Invoke-WebRequest -Method Get -Uri $get -UseBasicParsing } while($r.StatusCode -in 202,204)
  return ($r.Content | ConvertFrom-Json)
}

function Unwrap($o){ if($o -and $o.result -and $o.result.result){ return $o.result.result } if($o -and $o.result){ return $o.result } return $o }

Write-Host "[1/4] Fetch walls on level '$LevelName'" -ForegroundColor Cyan
$w = Invoke-Mcp 'get_walls' @{ skip=0; count=1000 }
$walls = (Unwrap $w).walls
if(-not $walls){ throw 'No walls found' }
$wallIds = @($walls | ForEach-Object { $_.elementId })
$info = Invoke-Mcp 'get_element_info' @{ elementIds=$wallIds; rich=$true }
$els = (Unwrap $info).elements
if(-not $els){ throw 'Failed to retrieve wall info' }
$zMm = 200

# Partition walls roughly by orientation in XY
$vertical = @()
$horizontal = @()
foreach($e in $els){
  $bb=$e.bboxMm; if(-not $bb){ continue }
  $dx=[double]($bb.max.x - $bb.min.x)
  $dy=[double]($bb.max.y - $bb.min.y)
  $mx=[double](($bb.max.x + $bb.min.x)/2.0)
  $my=[double](($bb.max.y + $bb.min.y)/2.0)
  $lev = ''
  if($e.constraints -and $e.constraints.wall -and $e.constraints.wall.baseLevelName){ $lev = $e.constraints.wall.baseLevelName }
  if(($LevelName -ne '') -and ($lev -ne $LevelName)){ continue }
  if($dy -ge ($dx*2)){
    $vertical += [pscustomobject]@{ x=$mx; y=$my; dx=$dx; dy=$dy; id=$e.elementId }
  } elseif($dx -ge ($dy*2)){
    $horizontal += [pscustomobject]@{ x=$mx; y=$my; dx=$dx; dy=$dy; id=$e.elementId }
  }
}

if($vertical.Count -lt 2 -or $horizontal.Count -lt 2){ Write-Warning 'Not enough orthogonal walls to detect cells.'; }

# Build sorted unique centers
$vx = $vertical | ForEach-Object { [math]::Round($_.x, 0) } | Sort-Object -Unique
$hy = $horizontal | ForEach-Object { [math]::Round($_.y, 0) } | Sort-Object -Unique

Write-Host ("  -> vertical walls: {0}, horizontal walls: {1}" -f $vx.Count, $hy.Count) -ForegroundColor DarkCyan

function Midpoints($arr){
  $mids=@(); for($i=0; $i -lt $arr.Count-1; $i++){ $m=($arr[$i]+$arr[$i+1])/2.0; $span=[math]::Abs($arr[$i+1]-$arr[$i]); $mids += [pscustomobject]@{ mid=$m; span=$span; a=$arr[$i]; b=$arr[$i+1] } }; return $mids
}
$mxs = Midpoints $vx | Where-Object { $_.span -ge $MinCellSizeMm -and $_.span -le $MaxCellSizeMm }
$mys = Midpoints $hy | Where-Object { $_.span -ge $MinCellSizeMm -and $_.span -le $MaxCellSizeMm }

Write-Host ("[2/4] Candidate cells: X={0}, Y={1}" -f $mxs.Count, $mys.Count) -ForegroundColor Cyan

# Sample candidate points as midpoints of adjacent wall centers
$candidates = @()
foreach($mx in $mxs){
  foreach($my in $mys){
    $candidates += [pscustomobject]@{
      x = [double]$mx.mid
      y = [double]$my.mid
      z = [double]$zMm
      spanX = [double]$mx.span
      spanY = [double]$my.span
      bounds = @{ xA=$mx.a; xB=$mx.b; yA=$my.a; yB=$my.b }
    }
  }
}

if($candidates.Count -eq 0){ Write-Host 'No candidate cells.' -ForegroundColor Yellow; return }

Write-Host "[3/4] Fetch rooms and filter covered cells" -ForegroundColor Cyan
$roomsRes = Invoke-Mcp 'get_rooms' @{ skip=0; count=2000 }
$rooms = (Unwrap $roomsRes).rooms | Where-Object { $_.level -eq $LevelName }
if(-not $rooms){ $rooms=@() }

function Distance2D($x1,$y1,$x2,$y2){ return [math]::Sqrt((($x1-$x2)*($x1-$x2)) + (($y1-$y2)*($y1-$y2))) }

$covered = New-Object System.Collections.Generic.HashSet[int]
for($i=0; $i -lt $candidates.Count; $i++){
  $p=$candidates[$i]
  $nearest=[double]::PositiveInfinity
  foreach($r in $rooms){ if($r.center){ $d=Distance2D $p.x $p.y $r.center.x $r.center.y; if($d -lt $nearest){ $nearest=$d } } }
  # Threshold: half of min(spanX,spanY)
  $th = [math]::Min($p.spanX, $p.spanY) / 2.0
  if($nearest -le $th){ $null = $covered.Add($i) }
}

$voids = @()
for($i=0; $i -lt $candidates.Count; $i++){
  if(-not $covered.Contains($i)){
    $voids += $candidates[$i]
  }
}

Write-Host ("[4/4] Roomless candidates: {0}" -f $voids.Count) -ForegroundColor Cyan
$outDir = Resolve-Path (Join-Path $PSScriptRoot '..\Logs')
if(-not (Test-Path $outDir)){ New-Item -ItemType Directory -Path $outDir | Out-Null }
$outFile = Join-Path $outDir 'roomless_candidates.json'
@{ level=$LevelName; totalCandidates=$candidates.Count; roomless=$voids.Count; points=$voids } | ConvertTo-Json -Depth 6 | Set-Content -Path $outFile -Encoding UTF8
Write-Host ("Saved: {0}" -f $outFile) -ForegroundColor Green

# Print top items
$printCount = [math]::Min($OutputLimit, $voids.Count)
for($i=0; $i -lt $printCount; $i++){
  $p=$voids[$i]
  Write-Output ('- centerMm=({0:N1},{1:N1},{2:N1}) span=({3:N0}x{4:N0}) bounds(x:[{5:N0},{6:N0}] y:[{7:N0},{8:N0}])' -f $p.x, $p.y, $p.z, $p.spanX, $p.spanY, $p.bounds.xA, $p.bounds.xB, $p.bounds.yA, $p.bounds.yB)
}

