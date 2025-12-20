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
  $work = Resolve-Path (Join-Path $SCRIPT_DIR '..\..\Work')
  $cands = Get-ChildItem -LiteralPath $work -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*_$p" }
  $chosen = $null
  if($cands){ $chosen = ($cands | Where-Object { $_.Name -notlike 'Project_*' } | Select-Object -First 1); if(-not $chosen){ $chosen = $cands | Select-Object -First 1 } }
  if(-not $chosen){ $chosen = New-Item -ItemType Directory -Path (Join-Path $work ("Project_{0}" -f $p)) }
  $logs = Join-Path $chosen.FullName 'Logs'
  if(-not (Test-Path $logs)){ New-Item -ItemType Directory -Path $logs | Out-Null }
  return $logs
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

function Resolve-InstanceParamMethods($category, [int]$categoryId){
  $methods = New-Object System.Collections.Generic.List[string]
  $c = ''
  if($category){ $c = ("" + $category).ToLowerInvariant() }
  # Heuristics by category name
  if($c -match 'wall' -or $c -match '壁'){ $methods.Add('get_wall_parameters'); $methods.Add('list_wall_parameters') }
  elseif($c -match 'window' -or $c -match '窓' -or $c -match 'ｳｨﾝﾄﾞｳ'){ $methods.Add('get_window_parameters') }
  elseif($c -match 'door' -or $c -match '扉' -or $c -match 'ドア'){ $methods.Add('get_door_parameters') }
  elseif($c -match 'structural frame' -or $c -match 'structural framing' -or $c -match '構造フレーム'){ $methods.Add('get_structural_frame_parameters'); $methods.Add('list_structural_frame_parameters') }
  elseif($c -match 'structural column' -or $c -match '構造柱'){ $methods.Add('get_structural_column_parameters'); $methods.Add('list_structural_column_parameters') }
  elseif($c -match 'structural foundation' -or $c -match '構造基礎'){ $methods.Add('get_structural_foundation_parameters'); $methods.Add('list_structural_foundation_parameters') }
  elseif($c -match 'floor' -or $c -match '床'){ $methods.Add('get_floor_parameters') }
  elseif($c -match 'ceiling' -or $c -match '天井'){ $methods.Add('get_ceiling_parameters') }
  elseif($c -match 'railing' -or $c -match '手すり'){ $methods.Add('get_railing_parameters') }
  elseif($c -match 'stair' -or $c -match '階段'){ $methods.Add('get_stair_parameters') }
  elseif($c -match 'revision cloud' -or $c -match 'リビジョン雲'){ $methods.Add('get_revision_cloud_parameters') }
  elseif($c -match 'mass'){ $methods.Add('get_mass_instance_parameters') }
  elseif($c -match 'tag' -or $c -match 'タグ'){ $methods.Add('get_tag_parameters') }
  # Generic family instance fallback
  $methods.Add('get_family_instance_parameters')
  return ,$methods
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

Write-Host "[2/3] Resolving element info ..." -ForegroundColor Cyan
$infoPath = Join-Path $LOGS 'selected_element_info.json'
$infoObj = Invoke-Revit -method 'get_element_info' -paramsObj @{ elementIds = $elementIds; rich = $true } -outFile $infoPath
$info = Get-Payload $infoObj
$elements = @()
if($info -and $info.elements){ $elements = @($info.elements) } elseif($info -and $info.items){ $elements = @($info.items) }
if(-not $elements -or $elements.Count -eq 0){ Write-Error "get_element_info で要素情報を取得できませんでした。"; exit 3 }

function Extract-ParamMaps($payload){
  $params = @{}
  $display = @{}
  if($null -eq $payload){ return ,@($params, $display) }
  if($payload.params){ $params = $payload.params }
  elseif($payload.result -and $payload.result.params){ $params = $payload.result.params }
  if($payload.display){ $display = $payload.display }
  elseif($payload.result -and $payload.result.display){ $display = $payload.result.display }
  if((!$params -or $params.Count -eq 0) -and $payload.parameters){
    foreach($p in $payload.parameters){
      $n = $p.name
      if($null -ne $n){
        $params[$n] = $p.value
        if($p.displayValue){ $display[$n] = $p.displayValue }
      }
    }
  }
  return ,@($params, $display)
}

Write-Host "[3/3] Fetching instance parameters ..." -ForegroundColor Cyan
$instances = @()
$idx = 0
foreach($e in $elements){
  $idx++
  $eid = $e.elementId
  $cat = $e.category
  $cid = $null; if($e.PSObject.Properties.Name -contains 'categoryId'){ $cid = $e.categoryId }
  $methods = Resolve-InstanceParamMethods -category $cat -categoryId $cid
  $payload = $null
  $methodUsed = $null
  $ok = $false
  foreach($m in $methods){
    $outPath = Join-Path $LOGS ("element_{0}_{1}_parameters.json" -f $eid, ($m -replace '[^a-zA-Z0-9_]',''))
    try {
      $resp = Invoke-Revit -method $m -paramsObj @{ elementId = $eid } -outFile $outPath
      $pl = Get-Payload $resp
      $payload = $pl
      $methodUsed = $m
      $ok = $true
      break
    } catch {
      $payload = @{ ok = $false; error = $_.Exception.Message; tried = $m }
    }
  }
  $maps = Extract-ParamMaps -payload $payload
  $instances += [ordered]@{
    elementId = $eid
    category = $cat
    familyName = $e.familyName
    typeName = $e.typeName
    method = $methodUsed
    result = $payload
    params = $maps[0]
    display = $maps[1]
  }
}

$final = [ordered]@{
  ok = $true
  port = $Port
  selectedElementIds = $elementIds
  count = $instances.Count
  instances = $instances
}

$finalPath = Join-Path $LOGS 'selected_instance_parameters.json'
$final | ConvertTo-Json -Depth 100 | Out-File -FilePath $finalPath -Encoding utf8
Write-Host ("Saved: {0}" -f $finalPath) -ForegroundColor Green

# print
Get-Content -Path $finalPath -Encoding UTF8

