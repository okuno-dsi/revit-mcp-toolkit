param(
  [Parameter(Mandatory=$true)][string]$CsvPath,
  [int]$LeftPort = 5210,
  [int]$RightPort = 5211,
  [string]$BaseViewName = 'RSL1',
  [double]$PaddingMm = 150.0,
  [switch]$CleanExisting
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8='1'
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

$SCRIPT_DIR = $PSScriptRoot
$ROOT = (Resolve-Path (Join-Path $SCRIPT_DIR '..\\..')).Path
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'

function Call-Mcp { param([int]$Port,[string]$Method,[hashtable]$Params,[int]$W=180,[int]$T=600,[switch]$Force)
  $pjson = ($Params | ConvertTo-Json -Depth 100 -Compress)
  $args = @('--port',$Port,'--command',$Method,'--params',$pjson,'--wait-seconds',[string]$W)
  if($T -gt 0){ $args += @('--timeout-sec',[string]$T) }
  if($Force){ $args += '--force' }
  $tmp = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ('mcp_'+[System.IO.Path]::GetRandomFileName()+'.json'))
  $args += @('--output-file',$tmp)
  $null = & python -X utf8 $PY @args 2>$null
  $code=$LASTEXITCODE
  $txt=''; try{ $txt = Get-Content -LiteralPath $tmp -Raw -Encoding UTF8 } catch{}
  if(Test-Path $tmp){ Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
  if($code -ne 0){ throw "MCP failed ($Method): $txt" }
  if([string]::IsNullOrWhiteSpace($txt)){ throw "Empty MCP response ($Method)" }
  return ($txt | ConvertFrom-Json -Depth 400)
}

function Payload($obj){ if($obj.result -and $obj.result.result){ return $obj.result.result } elseif($obj.result){ return $obj.result } else { return $obj } }

function Resolve-CompareViewId([int]$Port){
  $names = @("$BaseViewName 相違", "$BaseViewName 相違 *")
  $lov = Payload (Call-Mcp $Port 'list_open_views' @{} 60 120 -Force)
  $vs = @($lov.views)
  $vid = 0
  foreach($n in $names){
    $v = $vs | Where-Object { $_.name -like $n } | Select-Object -First 1
    if($v){ $vid = [int]$v.viewId; break }
  }
  if($vid -le 0){
    # Try opening by expected exact name
    try { $null = Call-Mcp $Port 'open_views' @{ names=@("$BaseViewName 相違") } 60 120 -Force } catch {}
    $lov2 = Payload (Call-Mcp $Port 'list_open_views' @{} 60 120 -Force)
    $v2 = @($lov2.views) | Where-Object { $_.name -eq ("$BaseViewName 相違") } | Select-Object -First 1
    if($v2){ $vid = [int]$v2.viewId }
  }
  if($vid -le 0){ throw "Port ${Port}: 相違ビューが見つかりません（'$BaseViewName 相違'）" }
  try { $null = Call-Mcp $Port 'activate_view' @{ viewId=$vid } 60 120 -Force } catch {}
  return $vid
}

function Clean-CloudsInView([int]$Port,[int]$ViewId){
  try {
    $lr = Payload (Call-Mcp $Port 'list_revisions' @{ includeClouds=$true; cloudFields=@('elementId','viewId') } 60 240 -Force)
    $clouds = @(); foreach($rv in $lr.revisions){ if($rv.clouds){ $clouds += $rv.clouds } }
    $del = @($clouds | Where-Object { try { [int]$_.viewId -eq $ViewId } catch { $false } })
    foreach($c in $del){ try { $null = Call-Mcp $Port 'delete_revision_cloud' @{ elementId = [int]$c.elementId } 60 240 -Force } catch {} }
  } catch {}
}

if(-not (Test-Path -LiteralPath $CsvPath)){ throw "CSV not found: $CsvPath" }
$rows = Import-Csv -LiteralPath $CsvPath -Encoding UTF8
# Collect elementIds per port (unique)
$idsL = New-Object System.Collections.Generic.HashSet[int]
$idsR = New-Object System.Collections.Generic.HashSet[int]
foreach($r in $rows){
  try {
    $port = [int]$r.port; $id = [int]$r.elementId
    if($port -eq $LeftPort){ [void]$idsL.Add($id) }
    elseif($port -eq $RightPort){ [void]$idsR.Add($id) }
  } catch {}
}

$Lview = Resolve-CompareViewId -Port $LeftPort
$Rview = Resolve-CompareViewId -Port $RightPort
if($CleanExisting){ Clean-CloudsInView -Port $LeftPort -ViewId $Lview; Clean-CloudsInView -Port $RightPort -ViewId $Rview }

$createdL = 0; $createdR = 0
foreach($id in $idsL){
  try{ $res = Payload (Call-Mcp $LeftPort 'create_revision_cloud_for_element_projection' @{ viewId=$Lview; elementId=[int]$id; paddingMm=$PaddingMm; preZoom='element'; restoreZoom=$false; focusMarginMm=150; mode='aabb' } 480 1200 -Force); $cid=0; try{ $cid=[int]$res.cloudId } catch{}; if($cid -gt 0){ $createdL++ } } catch{}
}
foreach($id in $idsR){
  try{ $res = Payload (Call-Mcp $RightPort 'create_revision_cloud_for_element_projection' @{ viewId=$Rview; elementId=[int]$id; paddingMm=$PaddingMm; preZoom='element'; restoreZoom=$false; focusMarginMm=150; mode='aabb' } 480 1200 -Force); $cid=0; try{ $cid=[int]$res.cloudId } catch{}; if($cid -gt 0){ $createdR++ } } catch{}
}

$out = [pscustomobject]@{ ok=$true; leftViewId=$Lview; rightViewId=$Rview; leftClouds=$createdL; rightClouds=$createdR; leftCount=$idsL.Count; rightCount=$idsR.Count; csv=$CsvPath }
$out | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ROOT 'Work/clouds_marked_from_csv.json') -Encoding UTF8
Write-Host ("Marked clouds from CSV. LeftClouds="+$createdL+"/"+$($idsL.Count)+" RightClouds="+$createdR+"/"+$($idsR.Count)) -ForegroundColor Green
