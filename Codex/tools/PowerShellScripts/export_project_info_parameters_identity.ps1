# @feature: export project info parameters identity | keywords: スペース
param(
  [int]$Port = 5210,
  [string]$OutCsv = "Work/Project_5210/Logs/project_info_parameters_identity_5210.csv",
  [int]$WaitSec = 20,
  [int]$TimeoutSec = 60,
  [int]$PauseMsPer50 = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8 = '1'

$ROOT = (Resolve-Path (Join-Path $PSScriptRoot '..\\..')).Path
$PY = Join-Path $PSScriptRoot 'send_revit_command_durable.py'

function Call-Mcp([string]$Method,[hashtable]$Params){
  if(-not $Params){ $Params = @{} }
  $pjson = ($Params | ConvertTo-Json -Depth 100 -Compress)
  $tmp = [System.IO.Path]::GetTempFileName()
  try {
    & python -X utf8 $PY --port $Port --command $Method --params $pjson --wait-seconds $WaitSec --timeout-sec $TimeoutSec --output-file $tmp 2>$null
    $txt = Get-Content -LiteralPath $tmp -Raw -Encoding UTF8
    if([string]::IsNullOrWhiteSpace($txt)){ throw "Empty MCP response ($Method)" }
    return ($txt | ConvertFrom-Json -Depth 400)
  } finally { try { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue } catch {} }
}
function Payload($o){
  if ($null -eq $o) { return $null }
  try {
    $has1 = $false; try { $has1 = ($o.PSObject.Properties.Match('result').Count -gt 0) } catch { $has1 = $false }
    if ($has1) {
      $lvl1 = $o.PSObject.Properties['result'].Value
      $has2 = $false; try { $has2 = ($lvl1.PSObject.Properties.Match('result').Count -gt 0) } catch { $has2 = $false }
      if ($has2) {
        return $lvl1.PSObject.Properties['result'].Value
      }
      return $lvl1
    }
  } catch { }
  return $o
}

# Ensure project info json
$work = Join-Path $ROOT 'Work'
if(-not (Test-Path $work)){ [void](New-Item -ItemType Directory -Path $work) }
$projDir = Join-Path $work ("Project_{0}" -f $Port)
if(-not (Test-Path $projDir)){ [void](New-Item -ItemType Directory -Path $projDir) }
$logs = Join-Path $projDir 'Logs'
if(-not (Test-Path $logs)){ [void](New-Item -ItemType Directory -Path $logs) }
$jsonPath = Join-Path $logs ("project_info_{0}.json" -f $Port)
if(-not (Test-Path $jsonPath)){
  $null = Call-Mcp 'get_project_info' @{}
  # try to re-save explicitly
  $obj = Call-Mcp 'get_project_info' @{}
  ($obj | ConvertTo-Json -Depth 50) | Set-Content -LiteralPath $jsonPath -Encoding UTF8
}
$raw = Get-Content -LiteralPath $jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
$info = Payload $raw
$eid = 0; try { $eid = [int]$info.elementId } catch { throw 'elementId not resolved' }
$params = @(); try { $params = @($info.parameters) } catch { throw 'parameters not found' }

# Prepare CSV
$header = 'name,id,storageType,isReadOnly,units,value,display,isShared,isBuiltIn,guid,resolvedBy'
Set-Content -LiteralPath $OutCsv -Encoding UTF8BOM -Value $header

# Built-in quick fill
$builtins = $params | Where-Object { try { [int]$_.id -lt 0 } catch { $false } }
foreach($pr in $builtins){
  $row = '"{0}",{1},"{2}",{3},"{4}","{5}","{6}",{7},{8},,{9}' -f ($pr.name -replace '"','""'), $pr.id, $pr.storageType, ($pr.isReadOnly -as [bool]), ($pr.units -replace '"','""'), ($pr.value -replace '"','""'), ($pr.display -replace '"','""'), 'false','true','heuristic:id<0'
  Add-Content -LiteralPath $OutCsv -Value $row -Encoding UTF8
}

# Non built-in: query identity
$nonBuilt = $params | Where-Object { try { [int]$_.id -ge 0 } catch { $false } }
$n = 0
foreach($pr in $nonBuilt){
  $n++
  $payload = @{ target=@{ by='elementId'; value=$eid }; paramName=$pr.name }
  $obj = $null
  try { $obj = Call-Mcp 'get_parameter_identity' $payload } catch { $obj = $null }
  $isShared=$null; $isBuiltIn=$null; $guid=$null; $resolvedBy='name'
  if($obj){ $pay = Payload $obj; if($pay){ try { $isShared = $pay.parameter.isShared } catch {}; try { $isBuiltIn = $pay.parameter.isBuiltIn } catch {}; try { $guid = $pay.parameter.guid } catch {} } }
  $row = '"{0}",{1},"{2}",{3},"{4}","{5}","{6}",{7},{8},{9},"{10}"' -f ($pr.name -replace '"','""'), $pr.id, $pr.storageType, ($pr.isReadOnly -as [bool]), ($pr.units -replace '"','""'), ($pr.value -replace '"','""'), ($pr.display -replace '"','""'), ($isShared -as [bool]), ($isBuiltIn -as [bool]), $guid, $resolvedBy
  Add-Content -LiteralPath $OutCsv -Value $row -Encoding UTF8
  if(($n % 50) -eq 0){ Start-Sleep -Milliseconds $PauseMsPer50 }
}

Write-Host ("Saved: " + (Resolve-Path $OutCsv).Path) -ForegroundColor Green
