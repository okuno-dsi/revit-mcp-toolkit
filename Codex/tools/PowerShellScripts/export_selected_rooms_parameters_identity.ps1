# @feature: export selected rooms parameters identity | keywords: 部屋, スペース, ビュー, レベル
param(
  [int]$Port = 5210,
  [string]$OutCsv = "Projects/Project_5210/Logs/selected_rooms_parameters_identity_5210.csv",
  [string]$UnitsMode = "SI", # SI | Project | Raw | Both
  [switch]$AllRooms,
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

function ReadPayload($obj){
  if($null -eq $obj){ return $null }
  try{ if($obj.result -and $obj.result.result){ return $obj.result.result } }catch{}
  try{ if($obj.result){ return $obj.result } }catch{}
  return $obj
}

# 1) Resolve target room ids
$ids = @()
if(-not $AllRooms){
  $sel = Call-Mcp 'get_selected_element_ids' @{}
  try { $ids = @((ReadPayload $sel).elementIds) } catch { $ids = @() }
}

# 2) If empty selection and not AllRooms, fallback to all elements in active view (limit 200)
if(-not $AllRooms -and $ids.Count -eq 0){
  try{
    $cv = Call-Mcp 'get_current_view' @{}
    $viewId = (ReadPayload $cv).viewId; if(-not $viewId){ $viewId = $cv.result.result.viewId }
    $els = Call-Mcp 'get_elements_in_view' @{ viewId=$viewId; _shape=@{ idsOnly=$true; page=@{ limit=200 } } }
    $ids = @((ReadPayload $els).elementIds)
  } catch { $ids = @() }
}

# 3) Collect rooms
$rooms = @()
if($AllRooms){
  $gr = Call-Mcp 'get_rooms' @{ skip=0; count=2147483647 }
  $data = $gr.result.result
  foreach($r in @($data.rooms)){
    $rooms += [pscustomobject]@{ elementId=$r.elementId; uniqueId=$r.uniqueId; level=$r.level }
  }
}
elseif($ids.Count -gt 0){
  for($i=0; $i -lt $ids.Count; $i+=100){
    $batch = $ids[$i..([Math]::Min($i+99,$ids.Count-1))]
    $info = Call-Mcp 'get_element_info' @{ elementIds=$batch; rich=$true }
    $els = @((ReadPayload $info).elements)
    foreach($e in $els){ if($e.className -eq 'Room' -or $e.category -match '部屋'){ $rooms += $e } }
  }
}

# 4) Prepare CSV
$outDir = Split-Path -Parent $OutCsv
if(-not (Test-Path $outDir)){ [void](New-Item -ItemType Directory -Path $outDir) }
$header = 'roomElementId,roomUniqueId,roomName,roomNumber,levelName,paramKind,name,paramId,storageType,isReadOnly,unitSi,unitProject,valueSi,valueProject,display,isShared,isBuiltIn,guid,origin,groupEnum,groupUi,placement,attachedTo,parameterElementId,categories,allowVaryBetweenGroups,resolvedBy,unitsMode'
Set-Content -LiteralPath $OutCsv -Encoding UTF8BOM -Value $header

if(-not $rooms -or $rooms.Count -eq 0){ Write-Host ("Saved (header only): " + (Resolve-Path $OutCsv).Path) -ForegroundColor Green; exit 0 }

# Helper to get room name/number via quick lookup
function TryGetRoomField([int]$eid, [string]$paramName){
  try {
    $r = Call-Mcp 'get_parameter_identity' @{ target=@{ by='elementId'; value=$eid }; paramName=$paramName; attachedToOverride='instance'; fields=@('name','value') }
    $p = (ReadPayload $r).parameter
    if($p -and $p.value){ return [string]$p.value.display }
  } catch { }
  return ''
}

$n = 0
foreach($room in $rooms){
  $eid = [int]$room.elementId
  $uid = $room.uniqueId
  $rname = TryGetRoomField $eid '部屋名'
  if([string]::IsNullOrWhiteSpace($rname)){ $rname = TryGetRoomField $eid 'Name' }
  $rnum = TryGetRoomField $eid '番号'
  if([string]::IsNullOrWhiteSpace($rnum)){ $rnum = TryGetRoomField $eid 'Number' }
  $lvl = $room.level

  # enumerate parameters (instance only for rooms)
  $meta = Call-Mcp 'get_param_meta' @{ target=@{ by='elementId'; value=$eid }; include=@{ instance=$true; type=$false }; maxCount=0 }
  $pars = @((ReadPayload $meta).parameters)
  foreach($pm in $pars){
    $kind = $pm.kind
    $bip = $pm.id
    $pname = $pm.name
    $req = @{ target=@{ by='elementId'; value=$eid }; fields=@('name','paramId','storageType','isReadOnly','isShared','isBuiltIn','guid','origin','group','placement','attachedTo','parameterElementId','categories','allowVaryBetweenGroups','value'); unitsMode=$UnitsMode; attachedToOverride='instance' }
    if([int]$bip -lt 0){ $req['builtInId'] = [int]$bip } else { $req['paramName'] = $pname }

    $res = Call-Mcp 'get_parameter_identity' $req
    $pay = ReadPayload $res
    $par = $pay.parameter
    $resolvedBy = $pay.resolvedBy
    if(-not $par){ continue }

    $unitSi=''; $unitProject=''; $valueSi=''; $valueProject=''; $disp=''
    try{
      $v = $par.value
      if($v){
        if($v.PSObject.Properties.Match('unitSi').Count -gt 0){
          $unitSi = [string]$v.unitSi
          $unitProject = [string]$v.unitProject
          $valueSi = [string]$v.valueSi
          $valueProject = [string]$v.valueProject
          $disp = [string]$v.display
        } else {
          # Fallback for legacy shape (unit/value/display only)
          $unitSi = ''
          $unitProject = [string]$v.unit
          $valueSi = ''
          $valueProject = [string]$v.value
          $disp = [string]$v.display
        }
      }
    } catch { }

    $catsJoined = ''
    try { if($par.categories){ $catsJoined = [string]::Join(';',$par.categories) } } catch {}

    $row = '"{0}","{1}","{2}","{3}","{4}","{5}","{6}",{7},"{8}",{9},"{10}","{11}","{12}","{13}","{14}",{15},{16},"{17}","{18}","{19}","{20}","{21}","{22}",{23},"{24}",{25},"{26}","{27}"' -f 
      $eid, ($uid -replace '"','""'), ($rname -replace '"','""'), ($rnum -replace '"','""'), ($lvl -replace '"','""'),
      $kind, (($par.name) -replace '"','""'), $par.paramId, $par.storageType, ($par.isReadOnly -as [bool]),
      ($unitSi -replace '"','""'), ($unitProject -replace '"','""'), ($valueSi -replace '"','""'), ($valueProject -replace '"','""'), ($disp -replace '"','""'),
      ($par.isShared -as [bool]), ($par.isBuiltIn -as [bool]), $par.guid, $par.origin, $par.group.enumName, ($par.group.uiLabel -replace '"','""'),
      ($par.placement -replace '"','""'), ($par.attachedTo -replace '"','""'), ($par.parameterElementId -as [int]),
      ($catsJoined -replace '"','""'), ($par.allowVaryBetweenGroups -as [bool]), ($resolvedBy -replace '"','""'), $UnitsMode
    Add-Content -LiteralPath $OutCsv -Value $row -Encoding UTF8

    $n++; if(($n % 50) -eq 0){ Start-Sleep -Milliseconds $PauseMsPer50 }
  }
}

Write-Host ("Saved: " + (Resolve-Path $OutCsv).Path) -ForegroundColor Green


