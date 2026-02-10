# @feature: place rooms from excel labels | keywords: 部屋, スペース, Excel, レベル
param(
  [int]$Port = 5210,
  [string]$ExcelUrl = 'http://localhost:5216',
  [string]$ExcelPath,
  [string]$SheetName = 'Sheet1',
  [double]$CellSizeMeters = 1.0,
  [string]$LevelName = '1FL',
  [switch]$UseColorMask,
  [int]$SleepMsBetween = 800
)

if(-not $ExcelPath){ throw 'ExcelPath is required' }
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8='1'

function Send-McpJson {
  param([string]$Method, [hashtable]$Params)
  $p = $Params | ConvertTo-Json -Depth 10 -Compress
  $out = & python (Join-Path (Split-Path $PSScriptRoot) 'send_revit_command_durable.py') --port $Port --command $Method --params $p 2>$null
  try { return ($out | ConvertFrom-Json) } catch { return $null }
}

function Unwrap($o){ if($o -and $o.result -and $o.result.result){ return $o.result.result } if($o -and $o.result){ return $o.result } return $o }

# 1) Resolve LevelId
$levels = Unwrap (Send-McpJson 'get_levels' @{ skip=0; count=2000 })
if(-not $levels -or -not $levels.levels){ throw 'get_levels failed' }
$level = @($levels.levels) | Where-Object { $_.name -eq $LevelName } | Select-Object -First 1
if(-not $level){ throw "Level not found: $LevelName" }
$levelId = [int]$level.levelId

# 2) Fetch labels from ExcelMCP
$body = @{ excelPath=$ExcelPath; sheetName=$SheetName; cellSizeMeters=$CellSizeMeters; useColorMask=([bool]$UseColorMask) } | ConvertTo-Json -Compress
$parse = Invoke-RestMethod -Method Post -Uri "$ExcelUrl/parse_plan" -ContentType 'application/json' -Body $body -TimeoutSec 60
$sr = $parse.scanRegion; $fc = $parse.originBorderCell
if(-not $sr -or -not $fc){ throw 'Missing scanRegion or originBorderCell' }
$cell = [double]$parse.cellSizeMeters
$heightCells = [int]$parse.heightCells
$cOff = [int]$fc.col - [int]$sr.firstCol
$rOff = [int]$fc.row - [int]$sr.firstRow
$offX_m = $cOff * $cell
$offY_m = ($heightCells - ($rOff + 1)) * $cell

$labels = @($parse.labels)
if(-not $labels -or $labels.Count -eq 0){ Write-Host '[Info] No labels found in Excel' -ForegroundColor Yellow; return }

# 3) Iterate labels: create one, verify with get_rooms, small sleep
$mm = 1000.0
$created = 0
$logs = @()
foreach($l in $labels){
  $name = [string]$l.text
  if([string]::IsNullOrWhiteSpace($name)){ continue }
  $lx = (([double]$l.x - $offX_m) * $mm)
  $ly = (([double]$l.y - $offY_m) * $mm)
  # center-of-cell adjustment: add 1/2 cell in both axes (requested)
  $centerShiftMm = ($cell * 1000.0) / 2.0
  $lx += $centerShiftMm
  $ly += $centerShiftMm

  $req = @{ levelId=$levelId; x=$lx; y=$ly; __smoke_ok=$true }
  $resRaw = Send-McpJson 'create_room' $req
  $res = Unwrap $resRaw
  $ok = ($res -and $res.ok -eq $true)
  # Server may return elementId or roomId depending on handler
  $rid = $null
  if($res -and $res.PSObject.Properties.Name -contains 'elementId'){ $rid = [int]$res.elementId }
  elseif($res -and $res.PSObject.Properties.Name -contains 'roomId'){ $rid = [int]$res.roomId }

  if($ok -and $rid){
    try { $null = Unwrap (Send-McpJson 'set_room_param' @{ elementId=$rid; paramName='Name'; value=$name; __smoke_ok=$true }) } catch {}
    $created++
  }

  # verify via get_rooms
  try { $rooms = Unwrap (Send-McpJson 'get_rooms' @{ skip=0; count=20000 }) } catch { $rooms = $null }
  $logs += [pscustomobject]@{ name=$name; x=$lx; y=$ly; ok=$ok; roomId=$rid; roomsCount=($rooms.rooms.Count) }
  Start-Sleep -Milliseconds ([int]$SleepMsBetween)
}

$logs | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $PSScriptRoot '..\Logs\rooms_from_labels_run.json') -Encoding UTF8
Write-Host ("Rooms created: {0}" -f $created) -ForegroundColor Green


