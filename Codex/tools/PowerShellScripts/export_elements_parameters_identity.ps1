# @feature: export elements parameters identity | keywords: スペース, ビュー
param(
  [int]$Port = 5210,
  [int[]]$ElementIds = @(),
  [string]$OutCsv = "Work/Project_5210/Logs/elements_parameters_identity_5210.csv",
  [string]$UnitsMode = "SI", # SI | Project | Raw | Both
  [int]$WaitSec = 20,
  [int]$TimeoutSec = 120,
  [int]$PauseMsPer50 = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8 = '1'

$ROOT = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
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

# Resolve element ids if not provided: use current view (limited to 200)
if(-not $ElementIds -or $ElementIds.Count -eq 0){
  $cv = Call-Mcp 'get_current_view' @{}
  $viewId = $cv.result.result.viewId
  $els = Call-Mcp 'get_elements_in_view' @{ viewId=$viewId; _shape=@{ idsOnly=$true; page=@{ limit=200 } } }
  $ElementIds = @($els.result.result.elementIds)
}
if(-not $ElementIds -or $ElementIds.Count -eq 0){ throw 'No elements to process.' }

# Prepare CSV (UTF-8 BOM)
$outDir = Split-Path -Parent $OutCsv
if(-not (Test-Path $outDir)){ [void](New-Item -ItemType Directory -Path $outDir) }
$header = 'elementId,paramKind,name,paramId,storageType,isReadOnly,units,value,display,isShared,isBuiltIn,guid,origin,groupEnum,groupUi,placement,attachedTo,parameterElementId,categories,allowVaryBetweenGroups,resolvedBy,unitsMode'
Set-Content -LiteralPath $OutCsv -Encoding UTF8BOM -Value $header

$n = 0
foreach($eid in $ElementIds){
  # 1) fetch full meta for instance and type
  $payload = Payload (Call-Mcp 'get_param_meta' @{ target=@{ by='elementId'; value=$eid }; include=@{ instance=$true; type=$true }; maxCount=0 })
  if(-not $payload){ continue }
  $params = @()
  try { $params = @($payload.parameters) } catch { $params = @() }
  if(-not $params -or $params.Count -eq 0){ continue }
  foreach($pm in $params){
    $n++
    $kind = $pm.kind # 'instance' | 'type'
    $bip = $pm.id
    $pname = $pm.name
    # Prefer builtInId when id is negative (built-in); otherwise use name
    $req = @{ target=@{ by='elementId'; value=$eid }; fields=@('name','paramId','storageType','isReadOnly','isShared','isBuiltIn','guid','origin','group','placement','attachedTo','parameterElementId','categories','allowVaryBetweenGroups','value'); unitsMode=$UnitsMode }
    if([int]$bip -lt 0){ $req['builtInId'] = [int]$bip } else { $req['paramName'] = $pname }
    if($kind -eq 'type'){ $req['attachedToOverride'] = 'type' } else { $req['attachedToOverride'] = 'instance' }

    $res = $null; try { $res = Call-Mcp 'get_parameter_identity' $req } catch { $res = $null }
    $pay = Payload $res
    $par = $null; $resolvedBy=''; $found=$false
    try { $par = $pay.parameter; $found = $pay.found } catch { }
    try { $resolvedBy = $pay.resolvedBy } catch { }
    if(-not $par){
      # row with minimal info
      $row = '"{0}","{1}","{2}",{3},"{4}",{5},"{6}","{7}","{8}",{9},{10},"{11}","{12}","{13}","{14}","{15}","{16}",{17},"{18}",{19},"{20}","{21}"' -f $eid,$kind,($pname -replace '"','""'),$bip,'', '', '', '', '', '','','','','','','','',0,'','',($resolvedBy -replace '"','""'),$UnitsMode
      Add-Content -LiteralPath $OutCsv -Value $row -Encoding UTF8
      continue
    }

    # Flatten
    $name = $par.name; $paramId = $par.paramId; $storage=$par.storageType; $isRO=$par.isReadOnly
    $isShared=$par.isShared; $isBuiltIn=$par.isBuiltIn; $guid=$par.guid; $origin=$par.origin
    $groupEnum = $par.group.enumName; $groupUi=$par.group.uiLabel
    $placement=$par.placement; $attached=$par.attachedTo
    $peid = $par.parameterElementId
    $cats = ''
    try { if($par.categories){ $cats = [string]::Join(';', $par.categories) } } catch { }
    $vary = $par.allowVaryBetweenGroups
    $units=''; $val=''; $disp=''; $raw=''
    try{
      $v=$par.value
      if($v){
        if($v.PSObject.Properties.Match('unitSi').Count -gt 0){
          $units = 'unitSi='+$v.unitSi+'; unitProject='+$v.unitProject
          $val = 'valueSi='+$v.valueSi+'; valueProject='+$v.valueProject
          $disp = $v.display; $raw = $v.raw
        } else {
          $units = $v.unit; $val = $v.value; $disp = $v.display; $raw = $v.raw
        }
      }
    } catch { }

    $row = '"{0}","{1}","{2}",{3},"{4}",{5},"{6}","{7}","{8}",{9},{10},"{11}","{12}","{13}","{14}","{15}","{16}",{17},"{18}",{19},"{20}","{21}"' -f 
      $eid, $kind, ($name -replace '"','""'), $paramId, $storage, ($isRO -as [bool]), ($units -replace '"','""'), ($val -replace '"','""'), ($disp -replace '"','""'), 
      ($isShared -as [bool]), ($isBuiltIn -as [bool]), $guid, $origin, $groupEnum, ($groupUi -replace '"','""'), $placement, $attached, ($peid -as [int]), ($cats -replace '"','""'), ($vary -as [bool]), ($resolvedBy -replace '"','""'), $UnitsMode
    Add-Content -LiteralPath $OutCsv -Value $row -Encoding UTF8

    if(($n % 50) -eq 0){ Start-Sleep -Milliseconds $PauseMsPer50 }
  }
}

Write-Host ("Saved: " + (Resolve-Path $OutCsv).Path) -ForegroundColor Green
