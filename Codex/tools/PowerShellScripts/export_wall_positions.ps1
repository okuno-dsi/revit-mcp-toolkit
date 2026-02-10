# @feature: export wall positions | keywords: 壁, スペース, レベル, スナップショット
param(
  [int]$Port = 5210,
  [string]$OutFile = '',
  [int]$BatchSize = 100
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
if(-not (Test-Path $logsDir)){ New-Item -ItemType Directory -Path $logsDir | Out-Null }
if([string]::IsNullOrWhiteSpace($OutFile)){
  $stamp = (Get-Date).ToString('yyyyMMdd_HHmmss')
  $OutFile = Join-Path $logsDir "walls_positions_${stamp}.json"
}

Write-Host "[1/3] Gather wall IDs" -ForegroundColor Cyan
$allWallIds = New-Object System.Collections.Generic.List[int]
$skip = 0
$pageSize = 200
$total = $null
do {
  $resObj = Invoke-McpCommand -Port $Port -Method 'get_walls' -Params @{ skip=$skip; count=$pageSize }
  $res = Unwrap-Result $resObj
  $walls = @($res.walls)
  if($null -eq $total -and $res.PSObject.Properties.Name -contains 'totalCount'){ $total = [int]$res.totalCount }
  foreach($w in $walls){ if($w -and $w.elementId){ [void]$allWallIds.Add([int]$w.elementId) } }
  $skip += $walls.Count
} while($walls.Count -gt 0 -and ($null -eq $total -or $skip -lt $total))
if($null -eq $total){ $total = $allWallIds.Count }
Write-Host ("  -> Walls: {0}" -f $allWallIds.Count) -ForegroundColor DarkCyan
if($allWallIds.Count -eq 0){ throw 'No walls found' }

Write-Host "[2/3] Fetch detailed info (position)" -ForegroundColor Cyan
$records = New-Object System.Collections.Generic.List[object]
for($i=0; $i -lt $allWallIds.Count; $i += $BatchSize){
  $chunk = $allWallIds[$i..([Math]::Min($i+$BatchSize-1, $allWallIds.Count-1))]
  $infoObj = Invoke-McpCommand -Port $Port -Method 'get_element_info' -Params @{ elementIds = $chunk; rich = $true }
  $info = Unwrap-Result $infoObj
  foreach($el in @($info.elements)){
    # Project the fields we care about
    $rec = [ordered]@{
      elementId = $el.elementId
      uniqueId = $el.uniqueId
      category = $el.category
      className = $el.className
      typeId = $el.typeId
      typeName = $el.typeName
      level = $el.level
      coordinatesMm = $el.coordinatesMm
      locationKind = $el.locationKind
      curveType = $el.curveType
      bboxMm = $el.bboxMm
      constraints = $el.constraints
    }
    $records.Add($rec) | Out-Null
  }
}

Write-Host "[3/3] Save JSON" -ForegroundColor Cyan
$snapshot = [ordered]@{
  savedAt = (Get-Date).ToString('s')
  port = $Port
  count = $records.Count
  note = 'Wall positions (mm). Includes bbox and basic constraints.'
  data = $records
}
$snapshot | ConvertTo-Json -Depth 8 | Set-Content -Path $OutFile -Encoding UTF8
Write-Host ("Saved: {0}" -f $OutFile) -ForegroundColor Green


