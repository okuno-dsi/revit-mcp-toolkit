# @feature: rename wall types by function | keywords: 壁, スペース, ビュー
param(
  [int]$Port = 5210,
  [int]$ViewId,
  [string]$PrefixExternal = '(外壁) ',
  [string]$PrefixInternal = '(内壁) ',
  [switch]$DryRun
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

function Ensure-ProjectDirByName([string]$projName, [int]$p){
  $workRoot = Resolve-Path (Join-Path $SCRIPT_DIR '..\\..\\..\\Projects')
  $cand = Get-ChildItem -LiteralPath $workRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq ("{0}_{1}" -f $projName, $p) }
  if(-not $cand){
    $cand = Get-ChildItem -LiteralPath $workRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like ("{0}_*" -f $projName) } | Select-Object -First 1
  }
  if(-not $cand){ $cand = New-Item -ItemType Directory -Path (Join-Path $workRoot ("{0}_{1}" -f $projName, $p)) }
  $logs = Join-Path $cand.FullName 'Logs'
  if(!(Test-Path $logs)){ [void](New-Item -ItemType Directory -Path $logs) }
  return @{ Root = $cand.FullName; Logs = $logs }
}

function Get-Payload($jsonObj){
  if($null -ne $jsonObj.result){ if($null -ne $jsonObj.result.result){ return $jsonObj.result.result } return $jsonObj.result }
  return $jsonObj
}

function Invoke-Revit($method, $paramsObj, $outFile){
  $paramsJson = if($null -ne $paramsObj) { ($paramsObj | ConvertTo-Json -Depth 20 -Compress) } else { '{}' }
  python $PY --port $Port --command $method --params $paramsJson --output-file $outFile | Out-Null
  if(!(Test-Path $outFile)){ throw "Expected output file not found: $outFile" }
  return (Get-Content -Raw -Encoding UTF8 -Path $outFile | ConvertFrom-Json)
}

function Resolve-ProjectDirs([int]$p){
  # get project name from Revit and map to Work folder
  $tmpDir = Join-Path $SCRIPT_DIR '..\Logs'
  if(!(Test-Path $tmpDir)){ [void](New-Item -ItemType Directory -Path $tmpDir) }
  $pi = Invoke-Revit -method 'get_project_info' -paramsObj @{ } -outFile (Join-Path $tmpDir 'project_info.temp.json')
  $ppl = Get-Payload $pi
  $name = ''
  try { $name = [string]$ppl.projectName } catch {}
  if([string]::IsNullOrWhiteSpace($name)){
    $od = Invoke-Revit -method 'get_open_documents' -paramsObj @{ } -outFile (Join-Path $tmpDir 'open_docs.temp.json')
    $o = Get-Payload $od
    if($o.documents){ $name = [string]$o.documents[0].title } else { $name = ("Project_{0}" -f $p) }
  }
  return Ensure-ProjectDirByName -projName $name -p $p
}

function Strip-Prefix([string]$name,[string[]]$prefixes){
  if([string]::IsNullOrWhiteSpace($name)){ return '' }
  $n = $name.TrimStart()
  # Exact prefixes list (ASCII parens + fullwidth parens + without parens)
  $cand = @()
  foreach($px in $prefixes){
    $cand += $px
    # Allow variants without trailing space
    if($px.EndsWith(' ')) { $cand += $px.Substring(0, $px.Length-1) }
    # Fullwidth paren variants
    if($px -match '^\(外壁\)'){ $cand += "（外壁） "; $cand += "（外壁）" }
    if($px -match '^\(内壁\)'){ $cand += "（内壁） "; $cand += "（内壁）" }
  }
  # Also allow plain words with space
  $cand += '外壁 ' ; $cand += '内壁 '
  foreach($p in $cand){ if($n.StartsWith($p)){ return $n.Substring($p.Length).TrimStart() } }
  return $name
}

if($useEnv){ Write-Host "[Port] Using REVIT_MCP_PORT=$Port" -ForegroundColor DarkCyan }
$dirs = Resolve-ProjectDirs -p $Port
Write-Host ("[Work] {0}" -f $dirs.Root) -ForegroundColor DarkCyan

# 1) Resolve view id
if(-not $ViewId -or $ViewId -le 0){
  $cv = Invoke-Revit -method 'get_current_view' -paramsObj @{ } -outFile (Join-Path $dirs.Logs 'current_view.json')
  $ViewId = [int](Get-Payload $cv).viewId
}
if($ViewId -le 0){ Write-Error "Invalid viewId=$ViewId"; exit 2 }

# 2) Get wall type ids (prefer in-view; fallback to all in project)
$gtivParams = @{ viewId = $ViewId; categories = @(-2000011); includeTypeInfo = $true; includeCounts = $true; modelOnly = $true }
$gtiv = Invoke-Revit -method 'get_types_in_view' -paramsObj $gtivParams -outFile (Join-Path $dirs.Logs 'wall_types_in_view.json')
$tv = Get-Payload $gtiv
$types = @(); if($tv -and $tv.types){ $types = @($tv.types) }
if($types.Count -eq 0){
  Write-Host "[Info] No wall types in view. Falling back to all wall types in project..." -ForegroundColor Yellow
  $allPath = Join-Path $dirs.Logs 'wall_types_all.json'
  $gt = Invoke-Revit -method 'get_wall_types' -paramsObj @{ skip=0; count=100000 } -outFile $allPath
  $tw = Get-Payload $gt
  if($tw -and $tw.types){ $types = @($tw.types) }
}
if($types.Count -eq 0){ Write-Host "[Info] No wall types found in project." -ForegroundColor Yellow; exit 0 }

$typeIds = @($types | ForEach-Object { if($_.typeId){ [int]$_.typeId } elseif($_.typeId -ne $null){ [int]$_.typeId } } | Sort-Object -Unique)

# 3) Bulk-get type parameters for Function (機能)
$tpParams = @{ typeIds = $typeIds; paramKeys = @(@{ name = 'Function' }, @{ name = '機能' }) }
$tp = Invoke-Revit -method 'get_type_parameters_bulk' -paramsObj $tpParams -outFile (Join-Path $dirs.Logs 'wall_type_params_function.json')
$tpb = Get-Payload $tp
$items = @(); if($tpb -and $tpb.items){ $items = @($tpb.items) }
if($items.Count -eq 0){ Write-Error "Failed to get type parameters (Function)."; exit 1 }

# 4) Decide new names by Function
$renamePlan = New-Object System.Collections.Generic.List[Object]

# Build name -> typeId map to detect conflicts
$nameToId = @{}
foreach($t in $types){
  $tn = ''
  try { $tn = [string]$t.typeName } catch {}
  if(-not [string]::IsNullOrWhiteSpace($tn)){
    if(-not $nameToId.ContainsKey($tn)){ $nameToId[$tn] = [int]$t.typeId }
  }
}
foreach($it in $items){
  $tid = [int]$it.typeId
  $currName = [string]$it.typeName
  if([string]::IsNullOrWhiteSpace($currName)){ $currName = "" }
  $disp = $it.display; $par = $it.params
  $fn = ''
  if($disp){
    $names = $disp.PSObject.Properties.Name
    if($names -contains 'Function'){ $fn = [string]($disp | Select-Object -ExpandProperty Function) }
    elseif($names -contains '機能'){ $fn = [string]($disp | Select-Object -ExpandProperty 機能) }
  }
  if([string]::IsNullOrWhiteSpace($fn) -and $par){
    $pnames = $par.PSObject.Properties.Name
    if($pnames -contains 'Function'){ $fn = [string]($par | Select-Object -ExpandProperty Function) }
    elseif($pnames -contains '機能'){ $fn = [string]($par | Select-Object -ExpandProperty 機能) }
  }
  $fnLower = $fn.ToLowerInvariant()
  $desiredPrefix = ''
  if($fnLower -match '外部' -or $fnLower -eq 'exterior'){ $desiredPrefix = $PrefixExternal }
  elseif($fnLower -match '内部' -or $fnLower -eq 'interior'){ $desiredPrefix = $PrefixInternal }
  else { continue }

  $stripped = Strip-Prefix -name $currName -prefixes @($PrefixExternal, $PrefixInternal)
  $newName = $desiredPrefix + $stripped
  if($newName -eq $currName){ continue }
  # Conflict check: if another type already has newName, skip to avoid collision
  if($nameToId.ContainsKey($newName) -and $nameToId[$newName] -ne $tid){
    continue
  }
  $renamePlan.Add([ordered]@{ typeId = $tid; oldName = $currName; newName = $newName; function = $fn }) | Out-Null
}

if($renamePlan.Count -eq 0){ Write-Host "[Info] All wall type names already consistent with Function parameter." -ForegroundColor Yellow; exit 0 }

Write-Host ("[Plan] Will rename {0} wall types" -f $renamePlan.Count) -ForegroundColor Cyan
foreach($p in $renamePlan){ Write-Host ("  - {0} -> {1}" -f $p.oldName, $p.newName) }

if($DryRun){ Write-Host "[DryRun] No changes sent." -ForegroundColor DarkYellow; exit 0 }

# 5) Execute renames
$renamed = New-Object System.Collections.Generic.List[Object]
$errors = New-Object System.Collections.Generic.List[Object]
foreach($p in $renamePlan){
  $payload = @{ typeId = [int]$p.typeId; newName = [string]$p.newName; __smoke_ok = $true }
  $out = Join-Path $dirs.Logs ("rename_wall_type_{0}.json" -f $p.typeId)
  try{
    $resp = Invoke-Revit -method 'rename_wall_type' -paramsObj $payload -outFile $out
    $renamed.Add([ordered]@{ typeId=$p.typeId; newName=$p.newName }) | Out-Null
  } catch {
    $errors.Add([ordered]@{ typeId=$p.typeId; error=$_.Exception.Message }) | Out-Null
  }
}

Write-Host ("[Done] Renamed {0} types. Errors: {1}" -f $renamed.Count, $errors.Count) -ForegroundColor Green


