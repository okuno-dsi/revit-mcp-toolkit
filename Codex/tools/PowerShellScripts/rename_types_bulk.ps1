# @feature: rename types bulk | keywords: misc
param(
  [int]$Port = 5210,
  [string]$CsvPath,
  [string]$JsonPath,
  [ValidateSet('skip','appendNumber','fail')][string]$ConflictPolicy = 'skip',
  [switch]$DryRun,
  [string]$ProjectName = 'BulkRename'
)

$ErrorActionPreference = 'Stop'
$useEnv = $false
if(-not $PSBoundParameters.ContainsKey('Port') -and $env:REVIT_MCP_PORT){
  try { $Port = [int]$env:REVIT_MCP_PORT; $useEnv = $true } catch {}
}

if(-not $CsvPath -and -not $JsonPath){ Write-Error 'Specify -CsvPath or -JsonPath.'; exit 2 }
if($CsvPath -and -not (Test-Path $CsvPath)){ Write-Error "CsvPath not found: $CsvPath"; exit 2 }
if($JsonPath -and -not (Test-Path $JsonPath)){ Write-Error "JsonPath not found: $JsonPath"; exit 2 }

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

function Invoke-Revit($method, $paramsObj, $outFile){
  $paramsJson = if($null -ne $paramsObj) { ($paramsObj | ConvertTo-Json -Depth 20 -Compress) } else { '{}' }
  python $PY --port $Port --command $method --params $paramsJson --output-file $outFile | Out-Null
  if(!(Test-Path $outFile)){ throw "Expected output file not found: $outFile" }
  return (Get-Content -Raw -Encoding UTF8 -Path $outFile | ConvertFrom-Json)
}

function Get-Body($jsonObj){ if($jsonObj.result){ if($jsonObj.result.result){ return $jsonObj.result.result } return $jsonObj.result }; return $jsonObj }

if($useEnv){ Write-Host "[Port] Using REVIT_MCP_PORT=$Port" -ForegroundColor DarkCyan }
$dirs = Ensure-ProjectDir -baseName $ProjectName -p $Port
Write-Host ("[Dirs] Using {0}" -f $dirs.Root) -ForegroundColor DarkCyan

# Load items
$items = @()
if($CsvPath){
  $rows = Import-Csv -Path $CsvPath
  foreach($r in $rows){
    $tid = $null; $uid = $null; $newName = $null
    if($r.PSObject.Properties.Name -contains 'typeId'){ try { $tid = [int]$r.typeId } catch {} }
    if($r.PSObject.Properties.Name -contains 'uniqueId'){ $uid = [string]$r.uniqueId }
    if($r.PSObject.Properties.Name -contains 'newName'){ $newName = [string]$r.newName }
    if((($tid -ne $null) -or ($uid)) -and $newName){ $items += @{ typeId = $tid; uniqueId = $uid; newName = $newName } }
  }
}
elseif($JsonPath){
  $obj = Get-Content -Raw -Encoding UTF8 $JsonPath | ConvertFrom-Json
  if($obj.items){ $items = @($obj.items) } else { $items = @($obj) }
}

if(-not $items -or $items.Count -eq 0){ Write-Error "No items loaded from mapping."; exit 2 }

# Page and call rename_types_bulk
$batch = 400
$processed=0; $renamed=0; $skipped=0; $i=0
for($s=0; $s -lt $items.Count; $s+=$batch){
  $i++
  $slice = $items[$s..([Math]::Min($s+$batch-1, $items.Count-1))]
  $paramObj = @{ items = $slice; conflictPolicy = $ConflictPolicy; dryRun = [bool]$DryRun.IsPresent; startIndex = 0; batchSize = $slice.Count }
  $out = Join-Path $dirs.Logs ("rename_types_bulk_{0:D2}.json" -f $i)
  $resp = Invoke-Revit -method 'rename_types_bulk' -paramsObj $paramObj -outFile $out
  $rb = Get-Body $resp
  $processed += [int]$rb.processed
  $renamed += [int]$rb.renamed
  $skipped += [int]$rb.skipped
}
Write-Host ("[Done] processed={0}, renamed={1}, skipped={2}" -f $processed,$renamed,$skipped) -ForegroundColor Green



