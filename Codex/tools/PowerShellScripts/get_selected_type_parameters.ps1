# @feature: get selected type parameters | keywords: 柱, 梁, 壁, スペース, 天井, 床
param(
  [int]$Port = 5210
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

function Resolve-LogsDir([int]$p){
  $work = Resolve-Path (Join-Path $SCRIPT_DIR '..\\..\\..\\Projects')
  $cands = Get-ChildItem -LiteralPath $work -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*_$p" }
  $chosen = $null
  if($cands){ $chosen = ($cands | Where-Object { $_.Name -notlike 'Project_*' } | Select-Object -First 1); if(-not $chosen){ $chosen = $cands | Select-Object -First 1 } }
  if(-not $chosen){ $chosen = New-Item -ItemType Directory -Path (Join-Path $work ("Project_{0}" -f $p)) }
  $logs = Join-Path $chosen.FullName 'Logs'
  if(-not (Test-Path $logs)){ New-Item -ItemType Directory -Path $logs | Out-Null }
  return $logs
}

function Get-Payload($jsonObj){
  # Durable outputs are JSON-RPC envelopes; prefer result.result when present
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

function Resolve-TypeParamMethod([string]$category){
  if([string]::IsNullOrWhiteSpace($category)){ return 'get_family_type_parameters' }
  $c = $category.ToLowerInvariant()
  if($c -match 'wall') { return 'get_wall_type_parameters' }
  if($c -match 'floor') { return 'get_floor_type_parameters' }
  if($c -match 'ceiling') { return 'get_ceiling_type_parameters' }
  if($c -match 'door') { return 'get_door_type_parameters' }
  if($c -match 'window') { return 'get_window_type_parameters' }
  if($c -match 'stair') { return 'get_stair_type_parameters' }
  if($c -match 'railing') { return 'get_railing_type_parameters' }
  if($c -match 'structural frame|structural framing') { return 'get_structural_frame_type_parameters' }
  if($c -match 'structural column') { return 'get_structural_column_type_parameters' }
  if($c -match 'structural foundation') { return 'get_structural_foundation_type_parameters' }
  if($c -match 'mass') { return 'get_mass_type_parameters' }
  if($c -match 'revision cloud') { return 'get_revision_cloud_type_parameters' }
  return 'get_family_type_parameters'
}

if($useEnv){ Write-Host "[Port] Using REVIT_MCP_PORT=$Port" -ForegroundColor DarkCyan }
$LOGS = Resolve-LogsDir -p $Port

Write-Host "[1/3] Fetching selected element IDs ..." -ForegroundColor Cyan
$selPath = Join-Path $LOGS 'selected_element_ids.json'
$selObj = Invoke-Revit -method 'get_selected_element_ids' -paramsObj @{ } -outFile $selPath
$sel = Get-Payload $selObj
$elementIds = @()
if($sel -and $sel.elementIds){ $elementIds = @($sel.elementIds | ForEach-Object { [int]$_ }) }
elseif($selObj -and $selObj.elementIds){ $elementIds = @($selObj.elementIds | ForEach-Object { [int]$_ }) }
if(-not $elementIds -or $elementIds.Count -eq 0){ Write-Error "Revit側で要素が選択されていません (get_selected_element_ids=empty)."; exit 3 }

Write-Host ("  selected count = {0}" -f $elementIds.Count) -ForegroundColor Gray

Write-Host "[2/3] Resolving types for selection ..." -ForegroundColor Cyan
$infoPath = Join-Path $LOGS 'selected_element_info.json'
$infoObj = Invoke-Revit -method 'get_element_info' -paramsObj @{ elementIds = $elementIds; rich = $true } -outFile $infoPath
$info = Get-Payload $infoObj
$elements = @()
if($info -and $info.elements){ $elements = @($info.elements) }
elseif($info -and $info.items){ $elements = @($info.items) }
elseif($infoObj -and $infoObj.result -and $infoObj.result.elements){ $elements = @($infoObj.result.elements) }
if(-not $elements -or $elements.Count -eq 0){ Write-Error "get_element_info で要素情報を取得できませんでした。"; exit 3 }

# Map unique typeIds
$typeMap = @{}
foreach($e in $elements){
  $tid = $null
  if($null -ne $e.typeId){ $tid = [int]$e.typeId }
  if(-not $tid -or $tid -le 0){ continue }
  if(-not $typeMap.ContainsKey($tid)){
    $typeMap[$tid] = [ordered]@{
      typeId = $tid
      categoryId = $e.categoryId
      category = $e.category
      familyName = $e.familyName
      typeName = $e.typeName
    }
  }
}

if($typeMap.Count -eq 0){ Write-Error "選択要素から typeId を解決できませんでした。"; exit 3 }
Write-Host ("  unique types = {0}" -f $typeMap.Count) -ForegroundColor Gray

Write-Host "[3/3] Fetching type parameters ..." -ForegroundColor Cyan
$typeResults = @()
foreach($kv in $typeMap.GetEnumerator()){
  $tid = [int]$kv.Key
  $meta = $kv.Value
  $method = Resolve-TypeParamMethod -category ([string]$meta.category)
  $outPath = Join-Path $LOGS ("type_{0}_parameters.json" -f $tid)
  try {
    $respObj = Invoke-Revit -method $method -paramsObj @{ typeId = $tid } -outFile $outPath
    $payload = Get-Payload $respObj
  } catch {
    $payload = @{ ok = $false; error = $_.Exception.Message }
  }
  $typeResults += [ordered]@{
    typeId = $tid
    category = $meta.category
    familyName = $meta.familyName
    typeName = $meta.typeName
    method = $method
    result = $payload
  }
}

# Consolidate and save
$final = [ordered]@{
  ok = $true
  port = $Port
  selectedElementIds = $elementIds
  typeCount = $typeResults.Count
  types = $typeResults
}

$finalPath = Join-Path $LOGS 'selected_type_parameters.json'
$final | ConvertTo-Json -Depth 50 | Out-File -FilePath $finalPath -Encoding utf8

Write-Host ("Saved: {0}" -f $finalPath) -ForegroundColor Green

# Also print to console
Get-Content -Path $finalPath -Encoding UTF8



