param(
  [int]$Port = 5210,
  [string]$LevelName = '1FL',
  [string]$TopLevelName,
  [double]$HeightMm,
  [string]$LogFile
)

$ErrorActionPreference = 'Stop'
chcp 65001 > $null

function Invoke-Mcp {
  param([string]$Method, [hashtable]$Params)
  $base = "http://localhost:$Port"
  $enqueue = "$base/enqueue"
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

$widthCells = [int]$root.widthCells
$heightCells = [int]$root.heightCells
$cellSizeMeters = [double]$root.cellSizeMeters
if($widthCells -le 0 -or $heightCells -le 0){ throw "Invalid grid size ($widthCells x $heightCells)" }

$Wm = $widthCells * $cellSizeMeters
$Hm = $heightCells * $cellSizeMeters
$Wmm = [math]::Round($Wm * 1000.0, 3)
$Hmm = [math]::Round($Hm * 1000.0, 3)

Write-Host "[Rect] ${widthCells}x${heightCells} cells, cell=${cellSizeMeters} m → ${Wmm} x ${Hmm} mm" -ForegroundColor DarkCyan

function New-Wall {
  param([double]$x1,[double]$y1,[double]$x2,[double]$y2)
  $p = @{ start=@{ x=$x1; y=$y1; z=0 }; end=@{ x=$x2; y=$y2; z=0 }; baseLevelName=$LevelName; __smoke_ok=$true }
  if($TopLevelName){ $p.topLevelName = $TopLevelName; $p.heightMm = 'level-to-level' }
  elseif($HeightMm -gt 0){ $p.heightMm = [double]$HeightMm }
  else{ $p.heightMm = 'level-to-level' }
  $res = Unwrap (Invoke-Mcp 'create_wall' $p)
  return $res
}

Write-Host "[Create] Perimeter walls (clockwise)" -ForegroundColor Green
$r1 = New-Wall -x1 0 -y1 0 -x2 $Wmm -y2 0
$r2 = New-Wall -x1 $Wmm -y1 0 -x2 $Wmm -y2 $Hmm
$r3 = New-Wall -x1 $Wmm -y1 $Hmm -x2 0 -y2 $Hmm
$r4 = New-Wall -x1 0 -y1 $Hmm -x2 0 -y2 0

$summary = [pscustomobject]@{ ok = ($r1.ok -and $r2.ok -and $r3.ok -and $r4.ok); edge1=$r1; edge2=$r2; edge3=$r3; edge4=$r4 }
$summary | ConvertTo-Json -Depth 12

