# @feature: color selected elements | keywords: ビュー
param(
  [int]$Port = 5210,
  [int]$R = 255,
  [int]$G = 0,
  [int]$B = 0,
  [int]$Transparency = 10,
  [string]$ProjectName = 'ColorSelection',
  [switch]$AutoWorkingView
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

if($useEnv){ Write-Host "[Port] Using REVIT_MCP_PORT=$Port" -ForegroundColor DarkCyan }
$dirs = Ensure-ProjectDir -baseName $ProjectName -p $Port
Write-Host ("[Dirs] Using {0}" -f $dirs.Root) -ForegroundColor DarkCyan

# 1) Get current selection
$selOut = Join-Path $dirs.Logs 'selected_element_ids.json'
$selObj = Invoke-Revit -method 'get_selected_element_ids' -paramsObj @{ } -outFile $selOut
$sel = if($selObj.result){ $selObj.result.result } else { $selObj }
$ids = @()
if($sel -and $sel.elementIds){ $ids = @($sel.elementIds | ForEach-Object { [int]$_ }) }
if(-not $ids -or $ids.Count -eq 0){ Write-Error "No elements selected in Revit (get_selected_element_ids returned empty)."; exit 3 }
Write-Host ("[Selection] Count={0}" -f $ids.Count) -ForegroundColor Cyan

# 2) Apply visual override per element (non-destructive)
$autoWv = $true
if($PSBoundParameters.ContainsKey('AutoWorkingView')){ $autoWv = [bool]$AutoWorkingView.IsPresent }
$results = New-Object System.Collections.Generic.List[Object]
$i = 0
foreach($eid in $ids){
  $i++
  $payload = @{ elementId = [int]$eid; r=$R; g=$G; b=$B; transparency=$Transparency; __smoke_ok=$true; autoWorkingView=$autoWv }
  $out = Join-Path $dirs.Logs ("set_visual_override_{0:D4}_{1}.json" -f $i, $eid)
  try {
    $resp = Invoke-Revit -method 'set_visual_override' -paramsObj $payload -outFile $out
    [void]$results.Add($resp)
  } catch {
    Write-Warning ("Failed to color elementId={0}: {1}" -f $eid, $_.Exception.Message)
  }
}

Write-Host ("[Done] Colored {0} elements (R={1},G={2},B={3},T={4}%)." -f $ids.Count, $R, $G, $B, $Transparency) -ForegroundColor Green
Write-Host ("Logs: {0}" -f $dirs.Logs) -ForegroundColor DarkGreen

