param(
  [int]$LeftPort = 5210,
  [int]$RightPort = 5211,
  [string]$BaseViewName = 'RSL1',
  [double]$PaddingMm = 150.0,
  [double]$PosTolMm = 10.0,
  [string[]]$TypeParamKeys = @('familyName','typeName','符号','H','B','tw','tf')
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
  $args = @('--port',$Port,'--command',$Method,'--params',$pjson,'--wait-seconds',[string]$W,'--timeout-sec',[string]$T)
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

function Get-SelectedId([int]$Port){
  $sel = Payload (Call-Mcp $Port 'get_selected_element_ids' @{} 60 120 -Force)
  $ids = @(); try { $ids = @($sel.elementIds) } catch {}
  if(-not $ids -or $ids.Count -eq 0){ throw "Port ${Port}: 選択が見つかりません" }
  return [int]$ids[0]
}

function Ensure-CompareView([int]$Port){
  $name = $BaseViewName; $dup = ($BaseViewName + ' 相違')
  try { $null = Call-Mcp $Port 'open_views' @{ names=@($name) } 60 120 -Force } catch {}
  $lov = Payload (Call-Mcp $Port 'list_open_views' @{} 60 120 -Force)
  $views = @($lov.views)
  $base = $views | Where-Object { $_.name -eq $name } | Select-Object -First 1
  if(-not $base){ throw "Port ${Port}: ベースビュー '${name}' が見つかりません" }
  $dupRes = Payload (Call-Mcp $Port 'duplicate_view' @{ viewId=[int]$base.viewId; withDetailing=$true; desiredName=$dup; onNameConflict='returnExisting'; idempotencyKey=("dup:{0}:{1}" -f $base.viewId,$dup) } 180 360 -Force)
  $vid = 0; try { $vid = [int]$dupRes.viewId } catch {}
  if($vid -le 0){ $lov2 = Payload (Call-Mcp $Port 'list_open_views' @{} 60 120 -Force); $v = @($lov2.views) | Where-Object { $_.name -eq $dup } | Select-Object -First 1; if($v){ $vid = [int]$v.viewId } }
  if($vid -le 0){ throw "Port ${Port}: 相違ビューIDが特定できません" }
  try { $null = Call-Mcp $Port 'set_view_template' @{ viewId=$vid; clear=$true } 60 120 -Force } catch {}
  try { $null = Call-Mcp $Port 'activate_view' @{ viewId=$vid } 60 120 -Force } catch {}
  return $vid
}

function Get-InfoAndType([int]$Port,[int]$ElemId){
  $info = Payload (Call-Mcp $Port 'get_element_info' @{ elementIds=@($ElemId); rich=$true } 180 600 -Force)
  $e = @($info.elements)[0]
  if(-not $e){ throw "Port ${Port}: 要素情報が取得できません (id=$ElemId)" }
  $tid = $null; try { $tid = [int]$e.typeId } catch {}
  $tpmap = @{}
  if($tid){
    $keys = @(); foreach($k in $TypeParamKeys){ if(-not [string]::IsNullOrWhiteSpace($k)){ $keys += @{ name = $k } } }
    $tp = Payload (Call-Mcp $Port 'get_type_parameters_bulk' @{ typeIds=@($tid); paramKeys=$keys; page=@{ startIndex=0; batchSize=100 } } 120 360 -Force)
    $item = $null; try { $item = @($tp.items)[0] } catch {}
    if($item){
      if($item.params){ foreach($n in $item.params.PSObject.Properties.Name){ $tpmap[$n] = [string]$item.params.$n } }
      if($item.display){ foreach($n in $item.display.PSObject.Properties.Name){ if(-not $tpmap.ContainsKey($n)){ $tpmap[$n] = [string]$item.display.$n } } }
    }
  }
  return @{ elem=$e; typeParams=$tpmap }
}

function CentroidMm($e){
  $bb = $e.bboxMm
  if($bb){
    try{ return @{ x = 0.5*([double]$bb.min.x + [double]$bb.max.x); y = 0.5*([double]$bb.min.y + [double]$bb.max.y); z = 0.5*([double]$bb.min.z + [double]$bb.max.z) } }catch{}
  }
  $cm = $e.coordinatesMm; if($cm){ return @{ x=[double]$cm.x; y=[double]$cm.y; z=[double]$cm.z } }
  return @{ x=0.0; y=0.0; z=0.0 }
}

function Diff-Selected($L,$R){
  $le=$L.elem; $re=$R.elem
  $lc = CentroidMm $le; $rc = CentroidMm $re
  $dx = [double]$lc.x - [double]$rc.x; $dy = [double]$lc.y - [double]$rc.y; $dz = [double]$lc.z - [double]$rc.z
  $dist = [Math]::Sqrt($dx*$dx + $dy*$dy + $dz*$dz)
  $diffs = @()
  foreach($k in $TypeParamKeys){
    $lv = $L.typeParams[$k]; $rv = $R.typeParams[$k]; if($lv -and $rv -and $lv -ne $rv){ $diffs += [pscustomobject]@{ key=$k; left=$lv; right=$rv } }
  }
  # family/type
  $lf = [string]$le.familyName; $rf = [string]$re.familyName; if($lf -ne $rf){ $diffs += [pscustomobject]@{ key='familyName'; left=$lf; right=$rf } }
  $lt = [string]$le.typeName; $rt = [string]$re.typeName; if($lt -ne $rt){ $diffs += [pscustomobject]@{ key='typeName'; left=$lt; right=$rt } }
  return @{ distanceMm=[Math]::Round($dist,1); withinTol=($dist -le $PosTolMm); diffs=$diffs; left=$le; right=$re; lc=$lc; rc=$rc }
}

try{
  $leftId  = Get-SelectedId -Port $LeftPort
  $rightId = Get-SelectedId -Port $RightPort
  $Lview = Ensure-CompareView -Port $LeftPort
  $Rview = Ensure-CompareView -Port $RightPort
  $L = Get-InfoAndType -Port $LeftPort -ElemId $leftId
  $R = Get-InfoAndType -Port $RightPort -ElemId $rightId
  $D = Diff-Selected -L $L -R $R
  # mark clouds on compare views regardless of withinTol (treat as 変更)
  $cmt = '差分: ' + ((@($D.diffs) | ForEach-Object { "{0}: {1}->{2}" -f $_.key,$_.left,$_.right }) -join '; ')
  $resL = Payload (Call-Mcp $LeftPort  'create_revision_cloud_for_element_projection' @{ viewId=$Lview; elementId=$leftId;  paddingMm=$PaddingMm; preZoom='element'; restoreZoom=$true; focusMarginMm=150; mode='aabb' } 480 1200 -Force)
  $resR = Payload (Call-Mcp $RightPort 'create_revision_cloud_for_element_projection' @{ viewId=$Rview; elementId=$rightId; paddingMm=$PaddingMm; preZoom='element'; restoreZoom=$true; focusMarginMm=150; mode='aabb' } 480 1200 -Force)
  $cl = 0; $cr = 0; try { $cl = [int]$resL.cloudId } catch {}; try { $cr = [int]$resR.cloudId } catch {}
  if($cl -gt 0){ try { $null = Call-Mcp $LeftPort  'set_revision_cloud_parameter' @{ elementId=$cl; paramName='Comments'; value=$cmt } 60 180 -Force } catch { try { $null = Call-Mcp $LeftPort  'set_revision_cloud_parameter' @{ elementId=$cl; paramName='コメント'; value=$cmt } 60 180 -Force } catch {} } }
  if($cr -gt 0){ try { $null = Call-Mcp $RightPort 'set_revision_cloud_parameter' @{ elementId=$cr; paramName='Comments'; value=$cmt } 60 180 -Force } catch { try { $null = Call-Mcp $RightPort 'set_revision_cloud_parameter' @{ elementId=$cr; paramName='コメント'; value=$cmt } 60 180 -Force } catch {} } }

  # CSV report with ports
  $ts = Get-Date -Format 'yyyyMMdd_HHmmss'
  $csv = Join-Path $ROOT ("Projects/selected_pair_marked_"+$ts+".csv")
  $rows = @(
    [pscustomobject]@{ port=$LeftPort;  elementId=[int]$L.elem.elementId; categoryId=[int]$L.elem.categoryId; familyName=$L.elem.familyName; typeName=$L.elem.typeName; cx=[double]$D.lc.x; cy=[double]$D.lc.y; cz=[double]$D.lc.z; distanceMm=[double]$D.distanceMm; withinTol=$D.withinTol; diffs=$cmt },
    [pscustomobject]@{ port=$RightPort; elementId=[int]$R.elem.elementId; categoryId=[int]$R.elem.categoryId; familyName=$R.elem.familyName; typeName=$R.elem.typeName; cx=[double]$D.rc.x; cy=[double]$D.rc.y; cz=[double]$D.rc.z; distanceMm=[double]$D.distanceMm; withinTol=$D.withinTol; diffs=$cmt }
  )
  $rows | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding UTF8
  Write-Host ("Clouds created. LeftCloudId=${cl} RightCloudId=${cr}`nCSV="+$csv) -ForegroundColor Green
}
catch{
  Write-Error $_
  exit 1
}

