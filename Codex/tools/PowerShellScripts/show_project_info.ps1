# @feature: show project info | keywords: スペース, ビュー
param(
  [int]$Port = 5210,
  [switch]$VerboseInfo
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
  $workRoot = Resolve-Path (Join-Path $SCRIPT_DIR '..\..\Work')
  # Prefer exact match directory under Work
  $cand = Get-ChildItem -LiteralPath $workRoot -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -eq $projName }
  if(-not $cand){
    # Try prefix match (ProjectName_*)
    $cand = Get-ChildItem -LiteralPath $workRoot -Directory -ErrorAction SilentlyContinue |
      Where-Object { $_.Name -like ("{0}*" -f $projName) } | Select-Object -First 1
  }
  if(-not $cand){
    # Fallback: create <ProjectName>_<Port>
    $cand = New-Item -ItemType Directory -Path (Join-Path $workRoot ("{0}_{1}" -f $projName, $p))
  }
  $logs = Join-Path $cand.FullName 'Logs'
  if(!(Test-Path $logs)){ [void](New-Item -ItemType Directory -Path $logs) }
  return @{ Root = $cand.FullName; Logs = $logs }
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

# 1) Fetch base info from Revit
$tmpDir = Join-Path $SCRIPT_DIR '..\Logs'
if(!(Test-Path $tmpDir)){ [void](New-Item -ItemType Directory -Path $tmpDir) }

$projObj = Invoke-Revit -method 'get_project_info' -paramsObj @{ } -outFile (Join-Path $tmpDir 'project_info.temp.json')
$proj = Get-Payload $projObj
$projectName = ''
try { $projectName = [string]$proj.projectName } catch {}
if([string]::IsNullOrWhiteSpace($projectName)){
  # Fallback to active document title
  $odObj = Invoke-Revit -method 'get_open_documents' -paramsObj @{ } -outFile (Join-Path $tmpDir 'open_docs.temp.json')
  $od = Get-Payload $odObj
  if($od -and $od.documents -and $od.documents.Count -gt 0){
    $projectName = [string]$od.documents[0].title
  } else {
    $projectName = ("Project_{0}" -f $Port)
  }
}

$dirs = Ensure-ProjectDirByName -projName $projectName -p $Port
Write-Host ("[Project] {0} -> {1}" -f $projectName, $dirs.Root) -ForegroundColor Cyan

# 2) Save details under Work/<ProjectName*>
$projPath = Join-Path $dirs.Logs 'project_info.json'
$openPath = Join-Path $dirs.Logs 'open_documents.json'
$viewPath = Join-Path $dirs.Logs 'current_view.json'

# Re-fetch and save to target
$projObj2 = Invoke-Revit -method 'get_project_info' -paramsObj @{ } -outFile $projPath
$openObj2 = Invoke-Revit -method 'get_open_documents' -paramsObj @{ } -outFile $openPath
$cvObj2 = Invoke-Revit -method 'get_current_view' -paramsObj @{ } -outFile $viewPath

$p = Get-Payload $projObj2
$o = Get-Payload $openObj2
$v = Get-Payload $cvObj2

# 3) Print concise summary
Write-Host "[Summary]" -ForegroundColor Green
Write-Host ("  Name   : {0}" -f ($p.projectName))
if($p.projectNumber){ Write-Host ("  Number : {0}" -f ($p.projectNumber)) }
if($p.client){ Write-Host ("  Client : {0}" -f ($p.client)) }
if($p.address){ Write-Host ("  Address: {0}" -f ($p.address)) }
Write-Host ("  Docs   : {0}" -f ($(if($o.documents){ $o.documents.Count } else { 0 })))
if($o.documents){
  $first = $o.documents | Select-Object -First 3
  foreach($d in $first){
    Write-Host ("    - {0} (workshared={1}, links={2})" -f $d.title, $d.isWorkshared, $d.linkCount)
  }
}
Write-Host ("  ViewId : {0}" -f ($v.viewId))

if($VerboseInfo){
  Write-Host "\n[Files]" -ForegroundColor DarkCyan
  Write-Host ("  - {0}" -f $projPath)
  Write-Host ("  - {0}" -f $openPath)
  Write-Host ("  - {0}" -f $viewPath)
}

