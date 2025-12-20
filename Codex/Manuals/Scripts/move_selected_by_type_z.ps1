param(
  [Parameter(Mandatory=$true)][string]$TypeName,
  [double]$DeltaZmm,
  [int]$Port = 5210,
  [switch]$Append
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8 = '1'
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

$SCRIPT_DIR = $PSScriptRoot
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'
if(!(Test-Path $PY)) { Write-Error "Python client not found: $PY"; exit 2 }

$workRoot = Resolve-Path (Join-Path $SCRIPT_DIR '..\..\Work')
$projDir = Join-Path $workRoot ("Move_Selected_{0}_{1}" -f $TypeName, $Port)
if(!(Test-Path $projDir)){ [void](New-Item -ItemType Directory -Path $projDir) }
$logs = Join-Path $projDir 'Logs'
if(!(Test-Path $logs)){ [void](New-Item -ItemType Directory -Path $logs) }

function Get-Payload($jsonObj){
  if($null -ne $jsonObj.result){
    if($null -ne $jsonObj.result.result){ return $jsonObj.result.result }
    return $jsonObj.result
  }
  return $jsonObj
}

function Invoke-Revit($method, $paramsObj, $outFile, [switch]$Force){
  $paramsJson = if($null -ne $paramsObj) { ($paramsObj | ConvertTo-Json -Depth 60 -Compress) } else { '{}' }
  $args = @('--port', $Port, '--command', $method, '--params', $paramsJson, '--output-file', $outFile)
  if($Force){ $args += '--force' }
  $null = & python -X utf8 $PY @args
  if(!(Test-Path $outFile)){ throw "Expected output file not found: $outFile" }
  return (Get-Content -Raw -Encoding UTF8 -Path $outFile | ConvertFrom-Json)
}

# 1) Get selected ids
$selOut = Join-Path $logs 'selected_ids.json'
$selResp = Invoke-Revit -method 'get_selected_element_ids' -paramsObj @{ } -outFile $selOut
$sel = Get-Payload $selResp
$ids = @(); try { $ids = @($sel.elementIds | ForEach-Object { [int]$_ }) } catch {}
if($ids.Count -eq 0){ Write-Error 'No elements are currently selected in Revit.'; exit 3 }
Write-Host ("[Selection] Selected count: {0}" -f $ids.Count) -ForegroundColor Cyan

# 2) Filter to matching typeName
$matches = New-Object System.Collections.Generic.List[Int32]
$chunk = 200
for($i=0; $i -lt $ids.Count; $i += $chunk){
  $hi = [Math]::Min($i+$chunk-1,$ids.Count-1)
  $batch = @($ids[$i..$hi])
  $infoOut = Join-Path $logs ("selected_info_{0}_{1}.json" -f $i, $batch.Count)
  $info = Invoke-Revit -method 'get_element_info' -paramsObj @{ elementIds = $batch; rich = $true } -outFile $infoOut
  $b = Get-Payload $info
  $elems = @()
  foreach($p in 'elements','result.elements','result.result.elements'){
    try { $cur = $b | Select-Object -ExpandProperty $p -ErrorAction Stop; $elems = @($cur); break } catch {}
  }
  foreach($e in $elems){
    $tn = ''
    try { $tn = [string]$e.typeName } catch {}
    if([string]::IsNullOrWhiteSpace($tn)){
      try { $tn = [string]$e.symbol.name } catch {}
      if([string]::IsNullOrWhiteSpace($tn)){
        try { $tn = [string]$e.type.name } catch {}
      }
    }
    if($tn -eq $TypeName){ try { [void]$matches.Add([int]$e.elementId) } catch {} }
  }
}

if($matches.Count -eq 0){ Write-Error ("No selected elements have TypeName == '{0}'." -f $TypeName); exit 4 }
Write-Host ("[Filter] Matched by TypeName == '{0}': {1}" -f $TypeName, $matches.Count) -ForegroundColor Yellow

if([double]::IsNaN($DeltaZmm)){
  Write-Error 'DeltaZmm is required (e.g., -150 for down)'; exit 2
}

$moved = 0
foreach($id in $matches){
  $moveOut = Join-Path $logs ("move_{0}.json" -f [int]$id)
  $params = @{ elementId = [int]$id; offset = @{ x = 0; y = 0; z = [double]$DeltaZmm }; __smoke_ok = $true }
  $resp = Invoke-Revit -method 'move_structural_frame' -paramsObj $params -outFile $moveOut -Force
  $moved++
}

Write-Host ("[Done] Moved {0} '{1}' elements by ΔZ={2} mm" -f $moved, $TypeName, [double]$DeltaZmm) -ForegroundColor Green

