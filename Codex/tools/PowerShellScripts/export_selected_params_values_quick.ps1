# @feature: export selected params values quick | keywords: スペース
param(
  [int]$Port = 5210
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
    & python -X utf8 $PY --port $Port --command $Method --params $pjson --wait-seconds 5 --timeout-sec 15 --output-file $tmp 2>$null | Out-Null
    $txt = Get-Content -LiteralPath $tmp -Raw -Encoding UTF8
    return ($txt | ConvertFrom-Json -Depth 400)
  } finally { try{ Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue } catch{} }
}
function Payload($o){ if($null -eq $o){ return $null } try{ $r=$o.result; $r2=$r.result; if($r2){ return $r2 } if($r){ return $r } } catch{} return $o }

# Paths
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

$sel = Payload (Call-Mcp 'get_selected_element_ids' @{})
$ElementIds = @(); try{ $ElementIds = @($sel.elementIds | ForEach-Object { [int]$_ }) } catch{}
if($ElementIds.Count -eq 0){ Write-Host '[INFO] No selection'; exit 0 }

$csv = Join-Path $logs ("selected_parameters_values_quick_{0}.csv" -f $Port)
Set-Content -LiteralPath $csv -Encoding UTF8BOM -Value 'port,elementId,where,name,paramId,storageType,isReadOnly,value,display'

foreach($eid in $ElementIds){
  $meta = Payload (Call-Mcp 'get_param_meta' @{ target=@{ by='elementId'; value=$eid }; include=@{ instance=$true; type=$true }; maxCount=0 })
  $plist = @(); try{ $plist = @($meta.parameters) } catch{}
  if($plist.Count -eq 0){ continue }
  $wants = New-Object System.Collections.ArrayList
  foreach($pm in $plist){ $id=[int]$pm.id; if($id -lt 0){ [void]$wants.Add($id) } else { [void]$wants.Add((''+$pm.name)) } }
  $vals = Payload (Call-Mcp 'get_param_values' @{ mode='element'; elementId=$eid; scope='auto'; includeMeta=$true; params=$wants })
  $values = @(); try{ $values = @($vals.values) } catch{}
  foreach($v in $values){
    $name=(''+$v.name); $pid=[int]$v.id; $st=(''+$v.storage); $ro=($v.isReadOnly -as [bool])
    $val=''; $disp=''; try{ if($null -ne $v.value){ $val = ''+$v.value } } catch{}; try{ if($null -ne $v.display){ $disp = ''+$v.display } } catch{}
    $row = '"{0}",{1},"{2}","{3}",{4},"{5}",{6},"{7}","{8}"' -f $Port,$eid,(''+$v.where),($name -replace '"','""'),$pid,$st,$ro,($val -replace '"','""'),($disp -replace '"','""')
    Add-Content -LiteralPath $csv -Value $row -Encoding UTF8
  }
}

Write-Host ("Saved: " + (Resolve-Path $csv).Path)
