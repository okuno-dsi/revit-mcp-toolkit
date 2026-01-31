# @feature: rename floor types by thickness | keywords: スペース, 床
param(
  [int]$Port = 5210,
  [switch]$DryRun,
  [string]$ProjectName = 'FloorThickness'
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
  $workRoot = Resolve-Path (Join-Path $SCRIPT_DIR '..\..\Work')
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

function StripThicknessPrefix([string]$name){
  if([string]::IsNullOrWhiteSpace($name)){ return '' }
  $n = $name.TrimStart()
  if($n.StartsWith('(')){
    $i = $n.IndexOf(')')
    if($i -gt 0){
      $inner = $n.Substring(1, $i-1).Replace(' ','')
      if($inner -match '^[0-9]+mm$'){ return $n.Substring($i+1).TrimStart() }
    }
  }
  return $name
}

if($useEnv){ Write-Host "[Port] Using REVIT_MCP_PORT=$Port" -ForegroundColor DarkCyan }
$dirs = Ensure-ProjectDir -baseName $ProjectName -p $Port
Write-Host ("[Dirs] Using {0}" -f $dirs.Root) -ForegroundColor DarkCyan

# 1) Get all floor types
$list = Invoke-Revit -method 'get_floor_types' -paramsObj @{ _shape = @{ page = @{ limit = 2000 } } } -outFile (Join-Path $dirs.Logs 'floor_types.list.json')
$lt = Get-Body $list
$types = @(); if($lt.floorTypes){ $types = @($lt.floorTypes) }
if($types.Count -eq 0){ Write-Host "[Info] No floor types." -ForegroundColor Yellow; exit 0 }

# Name set for conflicts
$nameSet = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
foreach($t in $types){ [void]$nameSet.Add([string]$t.typeName) }

# 2) Build plan using get_floor_type_info
$plan = New-Object System.Collections.Generic.List[Object]
foreach($t in $types){
  $info = Invoke-Revit -method 'get_floor_type_info' -paramsObj @{ typeId = [int]$t.typeId } -outFile (Join-Path $dirs.Logs ("floor_type.info.{0}.json" -f $t.typeId))
  $pi = Get-Body $info
  $mm = 0
  try { $mm = [int][math]::Round([double]$pi.thicknessMm) } catch { $mm = 0 }
  if($mm -le 0){ continue }
  $baseName = StripThicknessPrefix -name ([string]$t.typeName)
  $newName = "({0}mm) {1}" -f $mm, $baseName
  if($newName -ne $t.typeName -and -not $nameSet.Contains($newName)){
    $plan.Add([ordered]@{ typeId = [int]$t.typeId; oldName = [string]$t.typeName; newName = $newName; mm = $mm }) | Out-Null
    [void]$nameSet.Add($newName)
  }
}

if($plan.Count -eq 0){ Write-Host "[Info] No changes required (already prefixed or no thickness)." -ForegroundColor Yellow; exit 0 }

Write-Host ("[Plan] Rename {0} floor types" -f $plan.Count) -ForegroundColor Cyan
if($DryRun){
  $plan | ConvertTo-Json -Depth 6 | Out-File -FilePath (Join-Path $dirs.Logs 'rename_floor_types_by_thickness.plan.json') -Encoding UTF8
  Write-Host "[DryRun] Plan saved." -ForegroundColor DarkYellow
  exit 0
}

# 3) Execute renames
$ok=0; $fail=0
foreach($pItem in $plan){
  try{
    $resp = Invoke-Revit -method 'rename_floor_type' -paramsObj @{ typeId = [int]$pItem.typeId; newName = [string]$pItem.newName; __smoke_ok = $true } -outFile (Join-Path $dirs.Logs ("rename_floor_type.{0}.json" -f $pItem.typeId))
    $ok++
  } catch { $fail++ }
}
Write-Host ("[Done] Renamed={0}, Failed={1}" -f $ok, $fail) -ForegroundColor Green

