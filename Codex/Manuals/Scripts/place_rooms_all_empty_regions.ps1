param(
  [int]$Port = 5210,
  [string]$Prefix = 'AutoRoom',
  [int]$StartIndex = 1,
  [string]$LevelNamePattern = '',  # e.g. '1FL' or '.*' (regex when -UseRegex)
  [switch]$UseRegex,
  [switch]$DryRun,
  [int]$MaxPerLevel = 0  # 0 = unlimited
)

chcp 65001 > $null
$ErrorActionPreference = 'Stop'
$env:PYTHONUTF8='1'

function Unwrap-Top {
  param([object]$Obj)
  if($Obj -and $Obj.result -and $Obj.result.result){ return $Obj.result.result }
  if($Obj -and $Obj.result){ return $Obj.result }
  return $Obj
}

function Parse-JsonSafe {
  param([Parameter(ValueFromPipeline=$true)] [object]$Text)
  if($null -eq $Text){ return $null }
  $str = $null
  if($Text -is [string]){ $str = $Text }
  elseif($Text -is [System.Array]){ $str = [string]::Join("`n", $Text) }
  else { $str = [string]$Text }
  if([string]::IsNullOrWhiteSpace($str)){ return $null }
  $idx = $str.IndexOf('{')
  if($idx -lt 0){ return $null }
  $body = $str.Substring($idx)
  try { return ($body | ConvertFrom-Json) } catch { return $null }
}

function Call-Mcp {
  param([string]$Method, [hashtable]$Params)
  if(-not $Params){ $Params = @{} }
  $p = $Params | ConvertTo-Json -Compress -Depth 10
  $repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\\..')
  $py = Join-Path $repoRoot 'send_revit_command_durable.py'
  python -X utf8 $py --port $Port --command $Method --params $p
}

$logsDir = Resolve-Path (Join-Path $PSScriptRoot '..\Logs')
if(-not (Test-Path $logsDir)) { New-Item -ItemType Directory -Path $logsDir | Out-Null }
$levelsLog = Join-Path $logsDir 'levels_after.json'
$regionsLog = Join-Path $logsDir 'room_placeable_regions_all.json'
$resultsLog = Join-Path $logsDir 'place_rooms_batch_results.jsonl'
if(Test-Path $resultsLog){ Remove-Item $resultsLog -Force }

Write-Host "[1/4] Get levels" -ForegroundColor Cyan
$levelsOut = Call-Mcp -Method 'get_levels' -Params @{ skip=0; count=2000 }
$levelsOut | Out-File -FilePath $levelsLog -Encoding UTF8
$lvObj = Parse-JsonSafe $levelsOut
$lvTop = Unwrap-Top $lvObj
$levels = @($lvTop.levels)
if(-not $levels -or $levels.Count -eq 0){ throw 'No levels returned' }

if($LevelNamePattern){
  if($UseRegex){
    $levels = $levels | Where-Object { $_.name -match $LevelNamePattern }
  } else {
    $levels = $levels | Where-Object { $_.name -like $LevelNamePattern }
  }
}
if(-not $levels -or $levels.Count -eq 0){ throw "No levels matched by filter '$LevelNamePattern'" }

$names = $levels | ForEach-Object { $_.name } | Sort-Object
$quoted = $names | ForEach-Object { "'${_}'" }
$list = [string]::Join(', ', $quoted)
Write-Host ("  Levels targeted: {0}" -f $list) -ForegroundColor DarkCyan

Write-Host "[2/4] Find placeable regions (empty only)" -ForegroundColor Cyan
$allRegions = @()
foreach($level in $levels){
  $lid = [int]$level.levelId
  $regionsOut = Call-Mcp -Method 'find_room_placeable_regions' -Params @{ levelId=$lid; onlyEmpty=$true; includeLabelPoint=$true; includeLoops=$false }
  $rObj = Parse-JsonSafe $regionsOut
  $rtop = Unwrap-Top $rObj
  $regions = @($rtop.regions)
  if(-not $regions -or $regions.Count -eq 0){
    # fallback: fetch all and filter hasRoom later
    $regionsOut = Call-Mcp -Method 'find_room_placeable_regions' -Params @{ levelId=$lid; onlyEmpty=$false; includeLabelPoint=$true; includeLoops=$false }
    $rObj = Parse-JsonSafe $regionsOut
    $rtop = Unwrap-Top $rObj
    $regions = @($rtop.regions)
  }
  if($regions){
    $ix = 0
    foreach($rg in $regions){
      $ix++
      $allRegions += [pscustomobject]@{
        levelId      = $lid
        levelName    = [string]$level.name
        circuitIndex = [int]$rg.circuitIndex
        isClosed     = [bool]$rg.isClosed
        hasRoom      = $rg.PSObject.Properties.Name -contains 'hasRoom' ? [bool]$rg.hasRoom : $false
        area_m2      = $rg.PSObject.Properties.Name -contains 'area_m2' ? [double]$rg.area_m2 : $null
      }
    }
  }
}

$allRegions | ConvertTo-Json -Depth 6 | Set-Content -Path $regionsLog -Encoding UTF8
if(-not $allRegions -or $allRegions.Count -eq 0){ Write-Host 'No empty regions found.' -ForegroundColor Yellow; return }
Write-Host ("  Regions found (empty): {0}" -f $allRegions.Count) -ForegroundColor DarkCyan

Write-Host "[3/4] Place rooms" -ForegroundColor Cyan
$counter = [int]$StartIndex
$placed = 0; $failed = 0; $skipped = 0
$perLevelPlaced = @{}

# Group by level to honor MaxPerLevel if specified
$groups = $allRegions | Where-Object { $_.isClosed -eq $true -and ($_.hasRoom -eq $false) } | Group-Object levelId
foreach($g in $groups){
  $lid = [int]$g.Name
  $lname = ($g.Group | Select-Object -First 1).levelName
  $perLevelPlaced[$lid] = 0
  $limit = $MaxPerLevel
  foreach($rg in ($g.Group | Sort-Object circuitIndex)){
    if(($limit -gt 0) -and ($perLevelPlaced[$lid] -ge $limit)){
      $skipped++
      continue
    }
    $name = ('{0}-{1}' -f $Prefix, ($counter.ToString('000')))
    $payload = @{ levelId=$lid; circuitIndex=[int]$rg.circuitIndex; name=$name }
    if($DryRun){
      @{ action='DryRun'; params=$payload } | ConvertTo-Json -Compress | Out-File -Append -FilePath $resultsLog -Encoding utf8
      $counter++; $skipped++
      continue
    }
    try{
      $out = Call-Mcp -Method 'place_room_in_circuit' -Params $payload
      $obj = Parse-JsonSafe $out
      $top = Unwrap-Top $obj
      $row = [ordered]@{
        ok = $top.ok
        levelId = $lid
        levelName = $lname
        circuitIndex = [int]$rg.circuitIndex
        name = $name
        roomId = $top.roomId
      }
      ($row | ConvertTo-Json -Compress) | Out-File -Append -FilePath $resultsLog -Encoding utf8
      if($top.ok -eq $true){ $placed++; $perLevelPlaced[$lid] = $perLevelPlaced[$lid] + 1 } else { $failed++ }
      $counter++
    } catch {
      $failed++
      @{ ok=$false; levelId=$lid; levelName=$lname; circuitIndex=[int]$rg.circuitIndex; name=$name; error=$_.Exception.Message } | ConvertTo-Json -Compress | Out-File -Append -FilePath $resultsLog -Encoding utf8
      $counter++
    }
  }
}

Write-Host "[4/4] Summary" -ForegroundColor Cyan
Write-Host ("  Placed: {0}, Failed: {1}, Skipped: {2}" -f $placed, $failed, $skipped) -ForegroundColor Green
foreach($kv in $perLevelPlaced.GetEnumerator() | Sort-Object Name){
  $lname = ($levels | Where-Object { [int]$_.levelId -eq [int]$kv.Key } | Select-Object -First 1).name
  Write-Host ("   - Level {0} (#{1}): {2}" -f $lname, $kv.Key, $kv.Value) -ForegroundColor DarkGreen
}
Write-Host ("  Logs: levels={0}; regions={1}; results={2}" -f $levelsLog, $regionsLog, $resultsLog) -ForegroundColor DarkGray

