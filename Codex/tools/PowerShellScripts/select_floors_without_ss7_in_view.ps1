# @feature: select floors without ss7 in view | keywords: スペース, ビュー, 床
param(
  [int]$Port = 5210,
  [int]$ViewId,
  [string]$ProjectName = 'SelectFloors',
  [switch]$Append
)

$ErrorActionPreference = 'Stop'
$useEnv = $false
if(-not $PSBoundParameters.ContainsKey('Port') -and $env:REVIT_MCP_PORT){
  try { $Port = [int]$env:REVIT_MCP_PORT; $useEnv = $true } catch {}
}

chcp 65001 > $null
$env:PYTHONUTF8='1'

$SCRIPT_DIR = $PSScriptRoot
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'
if(!(Test-Path $PY)) { Write-Error "Python client not found: $PY"; exit 2 }

function Ensure-ProjectDir([string]$baseName, [int]$p){
  $workRoot = Resolve-Path (Join-Path $SCRIPT_DIR '..\\..\\..\\Projects')
  $projName = ("{0}_{1}" -f $baseName, $p)
  $projDir = Join-Path $workRoot $projName
  if(!(Test-Path $projDir)){ [void](New-Item -ItemType Directory -Path $projDir) }
  $logs = Join-Path $projDir 'Logs'
  if(!(Test-Path $logs)){ [void](New-Item -ItemType Directory -Path $logs) }
  return @{ Root = $projDir; Logs = $logs }
}

function Get-Payload($jsonObj){
  if($null -ne $jsonObj.result){
    if($null -ne $jsonObj.result.result){ return $jsonObj.result.result }
    return $jsonObj.result
  }
  return $jsonObj
}

function Invoke-Revit($method, $paramsObj, $outFile){
  $paramsJson = if($null -ne $paramsObj) { ($paramsObj | ConvertTo-Json -Depth 20 -Compress) } else { '{}' }
  python $PY --port $Port --command $method --params $paramsJson --output-file $outFile | Out-Null
  if(!(Test-Path $outFile)){ throw "Expected output file not found: $outFile" }
  return (Get-Content -Raw -Encoding UTF8 -Path $outFile | ConvertFrom-Json)
}

if($useEnv){ Write-Host "[Port] Using REVIT_MCP_PORT=$Port" -ForegroundColor DarkCyan }
$dirs = Ensure-ProjectDir -baseName $ProjectName -p $Port
Write-Host ("[Dirs] Using {0}" -f $dirs.Root) -ForegroundColor DarkCyan

# 1) Resolve view id
if(-not $ViewId -or $ViewId -le 0){
  $cvPath = Join-Path $dirs.Logs 'current_view.json'
  $cv = Invoke-Revit -method 'get_current_view' -paramsObj @{ } -outFile $cvPath
  $ViewId = [int](Get-Payload $cv).viewId
}
if($ViewId -le 0){ Write-Error "Invalid viewId=$ViewId"; exit 2 }
Write-Host ("[View] viewId={0}" -f $ViewId) -ForegroundColor Cyan

# 2) Gather elements in view (rows; page)
$rowsAll = New-Object System.Collections.Generic.List[Object]
$skip = 0; $count = 1000; $guard = 0
while($true){
  $p = @{ viewId = $ViewId; skip = $skip; count = $count }
  $out = Join-Path $dirs.Logs ("elements_in_view_rows_{0}_{1}.json" -f $skip, $count)
  $resp = Invoke-Revit -method 'get_elements_in_view' -paramsObj $p -outFile $out
  $b = Get-Payload $resp
  if($b.rows){
    $rows = @($b.rows)
    if($rows.Count -eq 0){ break }
    foreach($r in $rows){ [void]$rowsAll.Add($r) }
    $skip += $count
    if($b.totalCount -and $rowsAll.Count -ge $b.totalCount){ break }
  } elseif($b.elementIds){
    foreach($id in $b.elementIds){ [void]$rowsAll.Add([ordered]@{ elementId = [int]$id }) }
    break
  } else { throw "Unexpected shape for get_elements_in_view response." }
  if((++$guard) -ge 100){ break }
}

if($rowsAll.Count -eq 0){ Write-Host "[Info] No elements in the current view." -ForegroundColor Yellow; exit 0 }
Write-Host ("[View] Elements: {0}" -f $rowsAll.Count) -ForegroundColor Gray

# 3) Filter floors by category
function Is-FloorRow($row){
  try {
    if($row.PSObject.Properties.Name -contains 'categoryId'){
      if([int]$row.categoryId -eq -2000032){ return $true } # OST_Floors
    }
    if($row.PSObject.Properties.Name -contains 'categoryName'){
      $n = [string]$row.categoryName
      if($n -eq 'Floors' -or $n -eq '床'){ return $true }
      # some locales may use 'Floor'
      if($n -eq 'Floor'){ return $true }
    }
  } catch {}
  return $false
}

$floorIds = @($rowsAll | Where-Object { Is-FloorRow $_ } | ForEach-Object { [int]$_.elementId } | Sort-Object -Unique)
if($floorIds.Count -eq 0){ Write-Host "[Info] No Floors found in the current view." -ForegroundColor Yellow; exit 0 }
Write-Host ("[Floors] Candidate count: {0}" -f $floorIds.Count) -ForegroundColor Gray

# 4) Bulk get Comments and filter NOT containing SS7
$without = New-Object System.Collections.Generic.List[Int32]
$batchSize = 400
for($i=0; $i -lt $floorIds.Count; $i += $batchSize){
  $batch = $floorIds[$i..([Math]::Min($i+$batchSize-1, $floorIds.Count-1))]
  $paramsObj = @{ elementIds = $batch; paramKeys = @(@{ name = 'Comments' }, @{ name = 'コメント' }) }
  $out = Join-Path $dirs.Logs ("floor_inst_params_{0}_{1}.json" -f $i, $batch.Count)
  $resp = Invoke-Revit -method 'get_instance_parameters_bulk' -paramsObj $paramsObj -outFile $out
  $b = Get-Payload $resp
  if(-not $b.items){ continue }
  foreach($it in @($b.items)){
    $comments = ''
    $p = $it.params
    $d = $it.display
    if($p){
      $pnames = @(); try { $pnames = $p.PSObject.Properties.Name } catch {}
      if($pnames -contains 'Comments'){ $comments = [string]($p | Select-Object -ExpandProperty Comments) }
      elseif($pnames -contains 'コメント'){ $comments = [string]($p | Select-Object -ExpandProperty コメント) }
    }
    if([string]::IsNullOrWhiteSpace($comments) -and $d){
      $dnames = @(); try { $dnames = $d.PSObject.Properties.Name } catch {}
      if($dnames -contains 'Comments'){ $comments = [string]($d | Select-Object -ExpandProperty Comments) }
      elseif($dnames -contains 'コメント'){ $comments = [string]($d | Select-Object -ExpandProperty コメント) }
    }
    $hasSS7 = $false
    if(-not [string]::IsNullOrWhiteSpace($comments)){
      $hasSS7 = ($comments -like '*SS7*') -or ($comments.ToLowerInvariant().Contains('ss7'))
    }
    if(-not $hasSS7){ [void]$without.Add([int]$it.elementId) }
  }
}

$without = @([System.Linq.Enumerable]::Distinct($without))
Write-Host ("[Floors] Without SS7: {0}" -f $without.Count) -ForegroundColor Cyan
if($without.Count -eq 0){ Write-Host "[Info] No Floors without SS7 found." -ForegroundColor Yellow; exit 0 }

# 5) Select in Revit
$selParams = @{ elementIds = $without; replace = (-not $Append.IsPresent) }
$selOut = Join-Path $dirs.Logs 'select_floors_without_ss7.result.json'
$selResp = Invoke-Revit -method 'select_elements' -paramsObj $selParams -outFile $selOut
$sel = Get-Payload $selResp

Write-Host ("[Done] Selected {0} Floors (without SS7)." -f $without.Count) -ForegroundColor Green
Write-Host ("Result saved: {0}" -f $selOut) -ForegroundColor DarkGreen



