# @feature: apply from excelmcp | keywords: 壁, 部屋, スペース, タグ, Excel, レベル
param(
  [int]$Port = 5210,
  [string]$ExcelUrl = 'http://localhost:5216',
  [string]$ExcelPath,
  [string]$SheetName = 'Sheet1',
  [double]$CellSizeMeters = 1.0,
  [string]$LevelName = '1FL',
  [string]$WallTypeName = 'RC150',
  [double]$HeightMm = 3000,
  [switch]$UseColorMask
)

if(-not $ExcelPath){ throw 'ExcelPath is required' }

$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8='1'

function Merge-ColinearIntervals {
  param([array]$items, [ValidateSet('H','V')] [string]$axis)
  $groups = @{}
  foreach($it in $items){
    if($axis -eq 'H'){
      if([math]::Abs($it.y1 - $it.y2) -lt 1e-6){
        $y = [math]::Round($it.y1, 3)
        $a = [math]::Min($it.x1, $it.x2); $b = [math]::Max($it.x1, $it.x2)
        if(-not $groups.ContainsKey($y)){ $groups[$y] = @() }
        $groups[$y] += ,([pscustomobject]@{ a=$a; b=$b })
      }
    } else {
      if([math]::Abs($it.x1 - $it.x2) -lt 1e-6){
        $x = [math]::Round($it.x1, 3)
        $a = [math]::Min($it.y1, $it.y2); $b = [math]::Max($it.y1, $it.y2)
        if(-not $groups.ContainsKey($x)){ $groups[$x] = @() }
        $groups[$x] += ,([pscustomobject]@{ a=$a; b=$b })
      }
    }
  }
  $merged = @()
  foreach($k in $groups.Keys){
    $list = $groups[$k] | Sort-Object a
    if($list.Count -eq 0){ continue }
    $curA = $list[0].a; $curB = $list[0].b
    for($i=1;$i -lt $list.Count;$i++){
      $na = $list[$i].a; $nb = $list[$i].b
      if($na -le ($curB + 1.0)) { if($nb -gt $curB){ $curB = $nb } }
      else {
        if($axis -eq 'H'){ $merged += ,([pscustomobject]@{ kind='H'; y=$k; a=$curA; b=$curB }) }
        else { $merged += ,([pscustomobject]@{ kind='V'; x=$k; a=$curA; b=$curB }) }
        $curA = $na; $curB = $nb
      }
    }
    if($axis -eq 'H'){ $merged += ,([pscustomobject]@{ kind='H'; y=$k; a=$curA; b=$curB }) }
    else { $merged += ,([pscustomobject]@{ kind='V'; x=$k; a=$curA; b=$curB }) }
  }
  return ,$merged
}

# 1) Parse via ExcelMCP
$body = @{ excelPath=$ExcelPath; sheetName=$SheetName; cellSizeMeters=$CellSizeMeters; useColorMask=([bool]$UseColorMask) } | ConvertTo-Json -Compress
$parse = Invoke-RestMethod -Method Post -Uri "$ExcelUrl/parse_plan" -ContentType 'application/json' -Body $body -TimeoutSec 60
$parse | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $PSScriptRoot '..\Logs\parse_run.json') -Encoding UTF8

# 2) Origin shift to bottom-left border cell (requested)
$cell = [double]$parse.cellSizeMeters
$heightCells = [int]$parse.heightCells
$sr = $parse.scanRegion; $oc = $parse.originBorderCell
if($sr -and $oc){
  $cOff = [int]$oc.col - [int]$sr.firstCol
  $rOff = [int]$oc.row - [int]$sr.firstRow
  $offX_m = $cOff * $cell
  $offY_m = ($heightCells - ($rOff + 1)) * $cell
} else {
  # Fallback: global min of all segments
  $allSegs = @($parse.segmentsDetailed)
  if(-not $allSegs -or $allSegs.Count -eq 0){ throw 'No segments detected from ExcelMCP' }
  $minX = [double]::PositiveInfinity; $minY = [double]::PositiveInfinity
  foreach($s in $allSegs){
    $minX = [math]::Min($minX, [math]::Min([double]$s.x1, [double]$s.x2))
    $minY = [math]::Min($minY, [math]::Min([double]$s.y1, [double]$s.y2))
  }
  $offX_m = $minX; $offY_m = $minY
}

# 3) Project and merge
$mm = 1000.0
$proj = @($parse.segmentsDetailed) | ForEach-Object {
  [pscustomobject]@{
    kind=$_.kind;
    x1=([double]$_.x1 - $offX_m) * $mm;
    y1=([double]$_.y1 - $offY_m) * $mm;
    x2=([double]$_.x2 - $offX_m) * $mm;
    y2=([double]$_.y2 - $offY_m) * $mm;
  }
}
$h = Merge-ColinearIntervals -items $proj -axis 'H'
$v = Merge-ColinearIntervals -items $proj -axis 'V'
$segments = @(); foreach($h1 in $h){ $segments += ,([pscustomobject]@{ kind='H'; x1=$h1.a; y1=$h1.y; x2=$h1.b; y2=$h1.y }) }; foreach($v1 in $v){ $segments += ,([pscustomobject]@{ kind='V'; x1=$v1.x; y1=$v1.a; x2=$v1.x; y2=$v1.b }) }
$segments | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $PSScriptRoot '..\Logs\segments_merged.json') -Encoding UTF8

# 4) Create walls
$created=0; $errs=0; $elog=@()
foreach($s in $segments){
  $payload = @{ start=@{ x=[double]$s.x1; y=[double]$s.y1; z=0 }; end=@{ x=[double]$s.x2; y=[double]$s.y2; z=0 }; baseLevelName=$LevelName; heightMm=$HeightMm; wallTypeName=$WallTypeName; __smoke_ok=$true } | ConvertTo-Json -Compress
  $cmdOut = & python (Join-Path $PSScriptRoot 'send_revit_command_durable.py') --port $Port --command create_wall --params $payload 2>$null
  try{ $j=$cmdOut | ConvertFrom-Json } catch { $j=$null }
  $ok=$false; if($j -and $j.result -and $j.result.result -and $j.result.result.ok){ $ok=$true } elseif($j -and $j.result -and $j.result.ok){ $ok=$true }
  if($ok){ $created++ } else { $errs++; $elog += [pscustomobject]@{ seg=$s; resp=$cmdOut } }
}
if($elog.Count -gt 0){ $elog | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $PSScriptRoot '..\Logs\wall_errors.json') -Encoding UTF8 }

# 5) Place rooms at label points only, with label as Name
$labels = @($parse.labels)
$roomsPlaced=0; $rlog=@()
foreach($l in $labels){
  $lx = (([double]$l.x - $offX_m) * $mm)
  $ly = (([double]$l.y - $offY_m) * $mm)
  $name = [string]$l.text
  if([string]::IsNullOrWhiteSpace($name)){ continue }
  # Create with defaults: autoTag=true, strictEnclosure=true
  $payload = @{ levelName=$LevelName; x=$lx; y=$ly; __smoke_ok=$true } | ConvertTo-Json -Compress
  $createOut = & python (Join-Path $PSScriptRoot 'send_revit_command_durable.py') --port $Port --command create_room --params $payload 2>$null
  try{ $cj=$createOut | ConvertFrom-Json } catch { $cj=$null }
  $ok=$false; $elementId=$null
  if($cj -and $cj.result -and $cj.result.result -and $cj.result.result.ok){ $ok=$true; $elementId=$cj.result.result.elementId }
  elseif($cj -and $cj.result -and $cj.result.ok){ $ok=$true; $elementId=$cj.result.elementId }
  if($ok -and $elementId){
    $roomsPlaced++
    $setPayload=@{ elementId=[int]$elementId; paramName='Name'; value=$name; __smoke_ok=$true } | ConvertTo-Json -Compress
    $null = & python (Join-Path $PSScriptRoot 'send_revit_command_durable.py') --port $Port --command set_room_param --params $setPayload 2>$null
    $rlog += [pscustomobject]@{ name=$name; elementId=$elementId; x=$lx; y=$ly }
  } else {
    $rlog += [pscustomobject]@{ name=$name; error=$createOut }
  }
}

$summary = [pscustomobject]@{ wallsCreated=$created; wallErrors=$errs; roomsPlaced=$roomsPlaced }
$summary | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $PSScriptRoot '..\Logs\apply_summary.json') -Encoding UTF8
$rlog | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $PSScriptRoot '..\Logs\rooms_log.json') -Encoding UTF8
Write-Host ("[Summary] Walls={0}, Errors={1}, Rooms={2}" -f $created, $errs, $roomsPlaced) -ForegroundColor Green

