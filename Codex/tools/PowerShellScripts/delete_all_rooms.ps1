# @feature: delete all rooms | keywords: 部屋, スナップショット
param(
  [int]$Port = 5210,
  [switch]$WhatIf
)

chcp 65001 > $null
$ErrorActionPreference = 'Stop'

function Invoke-McpCommand {
  param(
    [int]$Port,
    [string]$Method,
    [hashtable]$Params,
    [int]$MaxAttempts = 120
  )
  $base = "http://localhost:$Port"
  $enqueue = "$base/enqueue"
  $get = "$base/get_result"
  $ts = [int64](([DateTimeOffset]::UtcNow).ToUnixTimeMilliseconds())
  if(-not $Params){ $Params = @{} }
  $payload = @{ jsonrpc='2.0'; method=$Method; params=$Params; id=$ts } | ConvertTo-Json -Depth 12 -Compress
  $headers = @{ Accept='application/json'; 'Accept-Charset'='utf-8' }

  # Enqueue
  $null = Invoke-RestMethod -Method Post -Uri $enqueue -Body $payload -ContentType 'application/json; charset=utf-8' -Headers $headers

  # Poll
  $attempts = 0
  while($attempts -lt $MaxAttempts){
    Start-Sleep -Milliseconds 500
    $r = Invoke-WebRequest -Method Get -Uri $get -UseBasicParsing -ErrorAction Stop
    if($r.StatusCode -in 202,204){ $attempts++; continue }
    return ($r.Content | ConvertFrom-Json)
  }
  throw "Timed out waiting for '$Method'"
}

function Unwrap-Result {
  param([object]$Obj)
  if($Obj -and $Obj.result -and $Obj.result.result){ return $Obj.result.result }
  if($Obj -and $Obj.result){ return $Obj.result }
  return $Obj
}

$logsDir = Resolve-Path (Join-Path $PSScriptRoot '..\Logs')
if(-not (Test-Path $logsDir)){
  New-Item -ItemType Directory -Path $logsDir | Out-Null
}
$snapshotFile = Join-Path $logsDir 'rooms_all_snapshot.json'
$resultLog = Join-Path $logsDir 'rooms_delete_results.jsonl'
if(Test-Path $resultLog){ Remove-Item $resultLog -Force }

Write-Host "[1/3] Query rooms" -ForegroundColor Cyan
$roomsObj = Invoke-McpCommand -Port $Port -Method 'get_rooms' -Params @{ skip=0; count=20000 }
$roomsRes = Unwrap-Result $roomsObj
$rooms = @($roomsRes.rooms)
if(-not $rooms -or $rooms.Count -eq 0){ Write-Host 'No rooms found' -ForegroundColor Yellow; return }

$snapshot = [ordered]@{
  savedAt = (Get-Date).ToString('s')
  totalRooms = $rooms.Count
  rooms = $rooms
}
$snapshot | ConvertTo-Json -Depth 8 | Set-Content -Path $snapshotFile -Encoding UTF8
Write-Host ("  -> Total={0}" -f $rooms.Count) -ForegroundColor DarkCyan
Write-Host ("  Snapshot: {0}" -f $snapshotFile) -ForegroundColor DarkGray

Write-Host "[2/3] Deleting rooms" -ForegroundColor Cyan
$ok=0; $failed=0; $skipped=0
foreach($r in $rooms){
  $eid = [int]$r.elementId
  if($eid -le 0){
    $failed++
    @{ elementId=$eid; error='Invalid elementId' } | ConvertTo-Json -Compress | Out-File -Append -FilePath $resultLog -Encoding utf8
    continue
  }
  if($WhatIf){
    $skipped++
    @{ elementId=$eid; action='WhatIf' } | ConvertTo-Json -Compress | Out-File -Append -FilePath $resultLog -Encoding utf8
    continue
  }
  try {
    $resObj = Invoke-McpCommand -Port $Port -Method 'delete_room' -Params @{ elementId = $eid }
    $payload = Unwrap-Result $resObj
    ($payload | ConvertTo-Json -Depth 8 -Compress) | Out-File -Append -FilePath $resultLog -Encoding utf8
    $ok++
  } catch {
    $failed++
    @{ elementId=$eid; error=$_.Exception.Message } | ConvertTo-Json -Compress | Out-File -Append -FilePath $resultLog -Encoding utf8
  }
}

Write-Host "[3/3] Summary" -ForegroundColor Cyan
Write-Host ("  Deleted: {0}, Failed: {1}, Skipped: {2}" -f $ok, $failed, $skipped) -ForegroundColor Green
Write-Host ("  Log: {0}" -f $resultLog) -ForegroundColor DarkGreen


