param(
  [int]$Port = 5210,
  [string]$TypeName = 'RC150',
  [int]$PageSize = 200,
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

Write-Host "[1/4] Resolving Wall Type: $TypeName" -ForegroundColor Cyan
$typesObj = Invoke-McpCommand -Port $Port -Method 'get_wall_types' -Params @{ skip=0; count=1000 }
$types = (Unwrap-Result $typesObj).types
if(-not $types){ throw 'No wall types returned from server' }
$target = $types | Where-Object { $_.typeName -eq $TypeName } | Select-Object -First 1
if(-not $target){ $target = $types | Where-Object { $_.typeName -like "*${TypeName}*" } | Select-Object -First 1 }
if(-not $target){ throw "Wall type matching '$TypeName' not found" }
$typeId = [int]$target.typeId
Write-Host ("  -> Found: {0} (typeId={1}, width={2})" -f $target.typeName, $typeId, $target.width) -ForegroundColor DarkCyan

Write-Host "[2/4] Listing All Walls" -ForegroundColor Cyan
$allWallIds = New-Object System.Collections.Generic.List[int]
$skip = 0
$total = $null
do {
  $resObj = Invoke-McpCommand -Port $Port -Method 'get_walls' -Params @{ skip=$skip; count=$PageSize }
  $res = Unwrap-Result $resObj
  $walls = @($res.walls)
  if($null -eq $total -and $res.PSObject.Properties.Name -contains 'totalCount'){ $total = [int]$res.totalCount }
  foreach($w in $walls){ if($w -and $w.elementId){ [void]$allWallIds.Add([int]$w.elementId) } }
  $skip += $walls.Count
} while($walls.Count -gt 0 -and ($null -eq $total -or $skip -lt $total))

if($null -eq $total){ $total = $allWallIds.Count }
Write-Host ("  -> Walls discovered: {0}" -f $allWallIds.Count) -ForegroundColor DarkCyan
if($allWallIds.Count -eq 0){ Write-Warning 'No walls to update. Exiting.'; return }

$logsDir = Resolve-Path (Join-Path $PSScriptRoot '..\Logs')
if(-not (Test-Path $logsDir)){ New-Item -ItemType Directory -Path $logsDir | Out-Null }
$logFile = Join-Path $logsDir "change_wall_type_to_${TypeName}.jsonl"
if(Test-Path $logFile){ Remove-Item $logFile -Force }

Write-Host "[3/4] Changing Wall Types" -ForegroundColor Cyan
$ok = 0; $failed = 0; $skipped = 0
foreach($eid in $allWallIds){
  if($WhatIf){
    $skipped++
    $entry = @{ elementId=$eid; action='WhatIf'; targetTypeId=$typeId }
    ($entry | ConvertTo-Json -Compress) | Out-File -FilePath $logFile -Append -Encoding utf8
    continue
  }
  try {
    $resObj = Invoke-McpCommand -Port $Port -Method 'change_wall_type' -Params @{ elementId = $eid; typeId = $typeId }
    $payload = Unwrap-Result $resObj
    $okFlag = $false
    if($payload -and $payload.PSObject.Properties.Name -contains 'ok'){
      $okFlag = [bool]$payload.ok
    } else {
      $okFlag = $true
    }
    if($okFlag){ $ok++ } else { $failed++ }
    ($payload | ConvertTo-Json -Depth 10 -Compress) | Out-File -FilePath $logFile -Append -Encoding utf8
  }
  catch {
    $failed++
    $err = @{ elementId=$eid; error=$_.Exception.Message }
    ($err | ConvertTo-Json -Compress) | Out-File -FilePath $logFile -Append -Encoding utf8
  }
}

Write-Host "[4/4] Summary" -ForegroundColor Cyan
Write-Host ("  Success: {0}, Failed: {1}, Skipped: {2}" -f $ok, $failed, $skipped) -ForegroundColor Green
Write-Host ("  Log: {0}" -f $logFile) -ForegroundColor DarkGreen

