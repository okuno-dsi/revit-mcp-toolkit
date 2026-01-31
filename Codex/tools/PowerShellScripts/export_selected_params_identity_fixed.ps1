# @feature: export selected params identity fixed | keywords: スペース
param(
  [int]$Port = 5210,
  [string]$UnitsMode = "Both", # SI | Project | Raw | Both
  [int]$WaitSec = 10,
  [int]$TimeoutSec = 25
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8 = '1'

$SCRIPT_DIR = $PSScriptRoot
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'
if(!(Test-Path $PY)){ throw "Python client not found: $PY" }

function Call-Mcp([string]$Method,[hashtable]$Params){
  if(-not $Params){ $Params=@{} }
  $pjson = ($Params | ConvertTo-Json -Depth 100 -Compress)
  $tmp = [System.IO.Path]::GetTempFileName()
  try{
    & python -X utf8 $PY --port $Port --command $Method --params $pjson --wait-seconds $WaitSec --timeout-sec $TimeoutSec --output-file $tmp 2>$null | Out-Null
    if(!(Test-Path $tmp)){ throw "Empty MCP response ($Method)" }
    $txt = Get-Content -LiteralPath $tmp -Raw -Encoding UTF8
    if([string]::IsNullOrWhiteSpace($txt)){ throw "Empty MCP response ($Method)" }
    return ($txt | ConvertFrom-Json -Depth 400)
  } finally { try{ Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue } catch{} }
}
function Payload($o){
  if($null -eq $o){ return $null }
  try{
    $has1 = ($o.PSObject.Properties.Match('result').Count -gt 0)
    if($has1){
      $lvl1 = $o.PSObject.Properties['result'].Value
      $has2 = ($lvl1.PSObject.Properties.Match('result').Count -gt 0)
      if($has2){ return $lvl1.PSObject.Properties['result'].Value }
      return $lvl1
    }
  } catch {}
  return $o
}

# Project folders
$proj = Payload (Call-Mcp 'get_project_info' @{})
$projectName = ''
try{ $projectName = [string]$proj.projectName } catch { $projectName = '' }
if([string]::IsNullOrWhiteSpace($projectName)){
  $od = Payload (Call-Mcp 'get_open_documents' @{})
  if($od -and $od.documents -and $od.documents.Count -gt 0){
    $projectName = ''+$od.documents[0].title
  } else { $projectName = ("Project_{0}" -f $Port) }
}
$inv = [IO.Path]::GetInvalidFileNameChars() -join ''
$re = New-Object System.Text.RegularExpressions.Regex("["+[RegEx]::Escape($inv)+"]")
$projectName = $re.Replace($projectName,'_')

$workRoot = (Resolve-Path (Join-Path $SCRIPT_DIR '..\..\Work')).Path
$projDir = Join-Path $workRoot ("{0}_{1}" -f $projectName,$Port)
$logs = Join-Path $projDir 'Logs'
if(!(Test-Path $logs)){ [void](New-Item -ItemType Directory -Path $logs) }

$sel = Payload (Call-Mcp 'get_selected_element_ids' @{})
$ElementIds = @(); try{ $ElementIds = @($sel.elementIds | ForEach-Object { [int]$_ }) } catch{}
if($ElementIds.Count -eq 0){ Write-Host '[INFO] No selection'; exit 0 }

$csv = Join-Path $logs ("selected_parameters_identity_{0}.csv" -f $Port)
Set-Content -LiteralPath $csv -Encoding UTF8BOM -Value 'elementId,paramKind,name,paramId,storageType,isReadOnly,units,value,display,isShared,isBuiltIn,guid,origin,groupEnum,groupUi,placement,attachedTo,parameterElementId,categories,allowVaryBetweenGroups,resolvedBy,unitsMode'

foreach($eid in $ElementIds){
  $meta = Payload (Call-Mcp 'get_param_meta' @{ target=@{ by='elementId'; value=$eid }; include=@{ instance=$true; type=$true }; maxCount=0 })
  $params = @(); try{ $params = @($meta.parameters) } catch{}
  if($params.Count -eq 0){ continue }

  foreach($pm in $params){
    $kind = ''+$pm.kind
    $bip = [int]$pm.id
    $pname = ''+$pm.name
    $req = @{ target=@{ by='elementId'; value=$eid }; fields=@('name','paramId','storageType','isReadOnly','isShared','isBuiltIn','guid','origin','group','placement','attachedTo','parameterElementId','categories','allowVaryBetweenGroups','value'); unitsMode=$UnitsMode }
    if($bip -lt 0){ $req['builtInId'] = $bip } else { $req['paramName'] = $pname }
    $req['attachedToOverride'] = ($kind -eq 'type') ? 'type' : 'instance'

    $res = $null
    try{ $res = Call-Mcp 'get_parameter_identity' $req } catch { $res = $null }
    $pay = Payload $res

    $par = $null; $resolvedBy=''
    try{ $par = $pay.parameter } catch {}
    try{ $resolvedBy = ''+$pay.resolvedBy } catch {}

    if(-not $par){
      $row = '"{0}","{1}","{2}",{3},"{4}",{5},"{6}","{7}","{8}",{9},{10},"{11}","{12}","{13}","{14}","{15}","{16}",{17},"{18}",{19},"{20}","{21}"' -f $eid,$kind,($pname -replace '"','""'),$bip,'', '', '', '', '', '','','','','','','','',0,'','',($resolvedBy -replace '"','""'),$UnitsMode
      Add-Content -LiteralPath $csv -Value $row -Encoding UTF8
      continue
    }

    $name = ''+$par.name
    $paramId = [int]$par.paramId
    $storage = ''+$par.storageType
    $isRO = ($par.isReadOnly -as [bool])
    $isShared = ($par.isShared -as [bool])
    $isBuiltIn = ($par.isBuiltIn -as [bool])
    $guid = ''+$par.guid
    $origin = ''+$par.origin
    $groupEnum = ''+$par.group.enumName
    $groupUi = ''+$par.group.uiLabel
    $placement = ''+$par.placement
    $attached = ''+$par.attachedTo
    $peid = ($par.parameterElementId -as [int])
    $cats = ''
    try{ if($par.categories){ $cats = [string]::Join(';',$par.categories) } } catch{}
    $vary = ($par.allowVaryBetweenGroups -as [bool])

    $units=''; $val=''; $disp=''; $raw=''
    try{
      $v = $par.value
      if($v){
        if($v.PSObject.Properties.Match('unitSi').Count -gt 0){
          $units = 'unitSi='+$v.unitSi+'; unitProject='+$v.unitProject
          $val = 'valueSi='+$v.valueSi+'; valueProject='+$v.valueProject
          $disp = ''+$v.display
          $raw = ''+$v.raw
        } else {
          $units = ''+$v.unit
          $val = ''+$v.value
          $disp = ''+$v.display
          $raw = ''+$v.raw
        }
      }
    } catch {}

    $row = '"{0}","{1}","{2}",{3},"{4}",{5},"{6}","{7}","{8}",{9},{10},"{11}","{12}","{13}","{14}","{15}","{16}",{17},"{18}",{19},"{20}","{21}"' -f 
      $eid, $kind, ($name -replace '"','""'), $paramId, $storage, $isRO, ($units -replace '"','""'), ($val -replace '"','""'), ($disp -replace '"','""'), 
      $isShared, $isBuiltIn, $guid, $origin, $groupEnum, ($groupUi -replace '"','""'), $placement, $attached, $peid, ($cats -replace '"','""'), $vary, ($resolvedBy -replace '"','""'), $UnitsMode
    Add-Content -LiteralPath $csv -Value $row -Encoding UTF8
  }
}

Write-Host ("Saved: " + (Resolve-Path $csv).Path)
