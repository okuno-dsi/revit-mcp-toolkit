# @feature: export selected params values guid | keywords: スペース
param(
  [int]$Port = 5210,
  [string]$UnitsMode = "Both", # SI | Project | Raw | Both
  [int]$WaitSec = 8,
  [int]$TimeoutSec = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8='1'

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
function Payload($o){ if($null -eq $o){ return $null } try{ $r=$o.result; $r2=$r.result; if($r2){ return $r2 } if($r){ return $r } } catch{} return $o }

# Project/paths
$proj = Payload (Call-Mcp 'get_project_info' @{})
$projectName = ''
try{ $projectName = ''+$proj.projectName } catch{}
if([string]::IsNullOrWhiteSpace($projectName)){
  $od = Payload (Call-Mcp 'get_open_documents' @{})
  if($od -and $od.documents -and $od.documents.Count -gt 0){ $projectName = ''+$od.documents[0].title } else { $projectName = ("Project_{0}" -f $Port) }
}
$inv = [IO.Path]::GetInvalidFileNameChars() -join ''
$re = New-Object System.Text.RegularExpressions.Regex("["+[RegEx]::Escape($inv)+"]")
$projectName = $re.Replace($projectName,'_')
$workRoot = (Resolve-Path (Join-Path $SCRIPT_DIR '..\..\Work')).Path
$projDir = Join-Path $workRoot ("{0}_{1}" -f $projectName,$Port)
$logs = Join-Path $projDir 'Logs'
if(!(Test-Path $logs)){ [void](New-Item -ItemType Directory -Path $logs) }

# Selection
$sel = Payload (Call-Mcp 'get_selected_element_ids' @{})
$ElementIds = @(); try{ $ElementIds = @($sel.elementIds | ForEach-Object { [int]$_ }) } catch{}
if($ElementIds.Count -eq 0){ Write-Host '[INFO] No selection'; exit 0 }

$csv = Join-Path $logs ("selected_parameters_with_values_guid_{0}.csv" -f $Port)
Set-Content -LiteralPath $csv -Encoding UTF8BOM -Value 'port,elementId,elementUniqueId,paramKind,name,paramId,storageType,value,display,isShared,isBuiltIn,guid,origin,groupEnum,groupUi,where,unitsMode'

foreach($eid in $ElementIds){
  # meta
  $meta = Payload (Call-Mcp 'get_param_meta' @{ target=@{ by='elementId'; value=$eid }; include=@{ instance=$true; type=$true }; maxCount=0 })
  $plist = @(); try{ $plist = @($meta.parameters) } catch{}
  if($plist.Count -eq 0){ continue }
  # build want list for get_param_values
  $wants = New-Object System.Collections.ArrayList
  foreach($pm in $plist){ $id=[int]$pm.id; if($id -lt 0){ [void]$wants.Add($id) } else { [void]$wants.Add((''+$pm.name)) } }
  # values
  $vals = Payload (Call-Mcp 'get_param_values' @{ mode='element'; elementId=$eid; scope='auto'; includeMeta=$true; params=$wants })
  $values = @(); try{ $values = @($vals.values) } catch{}
  # maps
  $vmap = @{}
  foreach($v in $values){ $key = (''+$v.name)+'|'+([int]$v.id)+'|'+(''+$v.where); $vmap[$key] = $v }
  # element info for uid
  $info = Payload (Call-Mcp 'get_element_info' @{ elementIds=@($eid); rich=$true })
  $uid = ''
  try{ $uid = ''+$info.elements[0].uniqueId } catch{}
  # resolve per param, and backfill GUID for shared if missing
  foreach($pm in $plist){
    $name=(''+$pm.name); $kind=(''+$pm.kind); $paramId=[int]$pm.id; $st=(''+$pm.storageType)
    $where = 'auto'
    # prefer meta kind as where fallback
    if($kind -eq 'type'){ $where='type' } elseif($kind -eq 'instance'){ $where='instance' }
    # get values
    $v = $null
    foreach($cand in @($where,'instance','type')){ $k = "$name|$paramId|$cand"; if($vmap.ContainsKey($k)){ $v = $vmap[$k]; $where=$cand; break } }
    $val=''; $disp=''
    if($v){ try{ if($null -ne $v.value){ $val = ''+$v.value } } catch{}; try{ if($null -ne $v.display){ $disp = ''+$v.display } } catch{} }
    # guid
    $guid = ''+($pm.guid)
    if(($pm.isShared -eq $true -or $pm.origin -eq 'shared') -and [string]::IsNullOrWhiteSpace($guid)){
      $att = ($kind -eq 'type') ? 'type' : 'instance'
      $req = @{ target=@{ by='elementId'; value=$eid }; attachedToOverride=$att }
      if($paramId -lt 0){ $req['builtInId'] = $paramId } else { $req['paramName'] = $name }
      try{ $idn = Payload (Call-Mcp 'get_parameter_identity' $req); $guid=''+$idn.guid; if([string]::IsNullOrWhiteSpace($guid)){ try{ $guid=''+$idn.parameter.guid } catch{} } } catch{}
    }
    $row = '"{0}",{1},"{2}","{3}","{4}",{5},"{6}","{7}","{8}",{9},{10},"{11}","{12}","{13}","{14}","{15}","{16}"' -f 
      $Port, $eid, ($uid -replace '"','""'), $kind, ($name -replace '"','""'), $paramId, $st, ($val -replace '"','""'), ($disp -replace '"','""'), 
      ($pm.isShared -as [bool]), ($pm.id -lt 0), ($guid -replace '"','""'), (''+$pm.origin), (''+$pm.projectGroup.enum), (''+($pm.projectGroup.uiLabel) -replace '"','""'), $where, $UnitsMode
    Add-Content -LiteralPath $csv -Value $row -Encoding UTF8
  }
}

Write-Host ("Saved: " + (Resolve-Path $csv).Path)
