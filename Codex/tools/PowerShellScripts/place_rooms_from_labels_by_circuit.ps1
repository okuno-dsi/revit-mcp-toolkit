# @feature: place rooms from labels by circuit | keywords: 部屋, Excel, レベル
param(
  [int]$Port = 5210,
  [string]$LevelName = '1FL',
  [string]$LogFile,
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
chcp 65001 > $null

function Invoke-Mcp {
  param([string]$Method, [hashtable]$Params)
  $base = "http://localhost:$Port"
  $enqueue = "$base/enqueue?force=1"
  $get = "$base/get_result"
  $ts = [int64](([DateTimeOffset]::UtcNow).ToUnixTimeMilliseconds())
  if(-not $Params){ $Params = @{} }
  $payload = @{ jsonrpc='2.0'; method=$Method; params=$Params; id=$ts } | ConvertTo-Json -Depth 50 -Compress
  $headers = @{ Accept='application/json'; 'Accept-Charset'='utf-8' }
  Invoke-RestMethod -Method Post -Uri $enqueue -Body $payload -ContentType 'application/json; charset=utf-8' -Headers $headers | Out-Null
  do { Start-Sleep -Milliseconds 400; $r = Invoke-WebRequest -Method Get -Uri $get -UseBasicParsing } while($r.StatusCode -in 202,204)
  return ($r.Content | ConvertFrom-Json)
}

function Unwrap($o){ if($o -and $o.result -and $o.result.result){ return $o.result.result } if($o -and $o.result){ return $o.result } return $o }

if(-not $LogFile){
  $logDir = Resolve-Path (Join-Path $PSScriptRoot '..\Logs')
  $last = Get-ChildItem $logDir -Filter 'excel_plan_import_*.json' | Sort-Object LastWriteTime | Select-Object -Last 1
  if(-not $last){ throw "No excel_plan_import_*.json found under $logDir" }
  $LogFile = $last.FullName
}

Write-Host "[Load] $LogFile" -ForegroundColor Cyan
$root = Get-Content $LogFile -Raw | ConvertFrom-Json | % { $_.result } | % { if($_.result){ $_.result } else { $_ } }
if(-not $root.ok){ throw ($root.msg ?? 'excel_plan_import result not ok') }

$labels = @($root.labels)
if(-not $labels -or $labels.Count -eq 0){ throw 'No labels in excel_plan_import result.' }
Write-Host ("[Labels] {0}" -f $labels.Count) -ForegroundColor DarkCyan

# Resolve levelId by name
$levelId = 0
try {
  $lvl = Unwrap (Invoke-Mcp 'get_levels' @{ skip=0; count=200 })
  foreach($lv in $lvl.levels){ if($lv.name -eq $LevelName){ $levelId = [int]$lv.levelId; break } }
} catch {}
if($levelId -le 0){
  # Fallback: read last saved levels log (levels_now.json)
  try {
    $levelsLog = Get-ChildItem (Resolve-Path (Join-Path $PSScriptRoot '..\Logs')) -Filter 'levels_*.json' | Sort-Object LastWriteTime | Select-Object -Last 1
    if($levelsLog){
      $lvj = Get-Content $levelsLog.FullName -Raw | ConvertFrom-Json | % { $_.result } | % { if($_.result){ $_.result } else { $_ } }
      foreach($lv in $lvj.levels){ if($lv.name -eq $LevelName){ $levelId = [int]$lv.levelId; break } }
    }
  } catch {}
}
if($levelId -le 0){ throw "Level '$LevelName' not found" }
Write-Host ("[Level] {0} (Id={1})" -f $LevelName,$levelId) -ForegroundColor DarkCyan

# Find room placeable regions (empty circuits)
$regions = (Unwrap (Invoke-Mcp 'find_room_placeable_regions' @{ levelId=$levelId; onlyEmpty=$true; coordUnits='mm'; includeLabelPoint=$true; includeLoops=$false })).regions
if(-not $regions){
  # Retry including occupied circuits; placement may fail for occupied ones
  $regions = (Unwrap (Invoke-Mcp 'find_room_placeable_regions' @{ levelId=$levelId; onlyEmpty=$false; coordUnits='mm'; includeLabelPoint=$true; includeLoops=$false })).regions
}
if(-not $regions){ throw 'No placeable regions found (both empty=false/true).' }
Write-Host ("[Regions] {0}" -f $regions.Count) -ForegroundColor DarkCyan

function Dist2([double]$x1,[double]$y1,[double]$x2,[double]$y2){ return [math]::Sqrt((($x1-$x2)*($x1-$x2)) + (($y1-$y2)*($y1-$y2))) }

$placed = @()
foreach($l in $labels){
  $name = [string]$l.text
  $xmm = [double]([math]::Round($l.x * 1000.0, 3))
  $ymm = [double]([math]::Round($l.y * 1000.0, 3))
  $best = $null; $bestD = 1e99
  foreach($rg in $regions){
    $lp = $rg.labelPoint
    if($lp){ $d = Dist2 $xmm $ymm ([double]$lp.x) ([double]$lp.y); if($d -lt $bestD){ $bestD=$d; $best=$rg } }
  }
  if(-not $best){ $placed += @{ ok=$false; name=$name; reason='no-near-region' }; continue }
  if($DryRun){ $placed += @{ ok=$true; dryRun=$true; name=$name; circuit=$best.circuitIndex; at=@{x=$xmm;y=$ymm} }; continue }
  $res = Unwrap (Invoke-Mcp 'place_room_in_circuit' @{ levelId=$levelId; circuitIndex=[int]$best.circuitIndex; name=$name; __smoke_ok=$true })
  if($res -and $res.ok){ $placed += @{ ok=$true; name=$name; roomId=$res.roomId; circuit=$best.circuitIndex }
  } else { $placed += @{ ok=$false; name=$name; error=($res.msg ?? 'place failed'); circuit=$best.circuitIndex } }
}

$placed | ConvertTo-Json -Depth 8

