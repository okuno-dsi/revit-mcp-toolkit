# @feature: compare selected across ports | keywords: スペース
param(
  [int]$LeftPort = 5210,
  [int]$RightPort = 5211,
  [double]$PosTolMm = 10.0,
  [string[]]$TypeParamKeys = @('符号','H','B','tw','tf')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8='1'
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

$SCRIPT_DIR = $PSScriptRoot
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'

function Call-Mcp { param([int]$Port,[string]$Method,[hashtable]$Params,[int]$W=120,[int]$T=300,[switch]$Force)
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

function Get-Selection([int]$Port){
  $sel = Payload (Call-Mcp $Port 'get_selected_element_ids' @{} 60 120 -Force)
  $ids = @(); try { $ids = @($sel.elementIds) } catch {}
  if(-not $ids -or $ids.Count -eq 0){ throw "Port ${Port}: 選択が見つかりません" }
  return @([int]$ids[0])
}

function Get-InfoAndType([int]$Port,[int]$ElemId,[string[]]$TypeKeys){
  $info = Payload (Call-Mcp $Port 'get_element_info' @{ elementIds=@($ElemId); rich=$true } 180 600 -Force)
  $e = @($info.elements)[0]
  if(-not $e){ throw "Port ${Port}: 要素情報が取得できません (id=$ElemId)" }
  $tid = $null; try { $tid = [int]$e.typeId } catch {}
  $tparams = $null
  if($tid -and $TypeKeys -and $TypeKeys.Count -gt 0){
    $keys = @(); foreach($k in $TypeKeys){ if(-not [string]::IsNullOrWhiteSpace($k)){ $keys += @{ name = $k } } }
    $tp = Payload (Call-Mcp $Port 'get_type_parameters_bulk' @{ typeIds=@($tid); paramKeys=$keys; page=@{ startIndex=0; batchSize=100 } } 120 360 -Force)
    try { $tparams = @($tp.items)[0] } catch { $tparams = $null }
  }
  return @{ elem=$e; typeParams=$tparams }
}

function CentroidMm($e){
  $bb = $e.bboxMm
  if($bb){
    try{
      $cx = 0.5*([double]$bb.min.x + [double]$bb.max.x)
      $cy = 0.5*([double]$bb.min.y + [double]$bb.max.y)
      $cz = 0.5*([double]$bb.min.z + [double]$bb.max.z)
      return @{ x=$cx; y=$cy; z=$cz }
    }catch{}
  }
  $cm = $e.coordinatesMm
  if($cm){ return @{ x=[double]$cm.x; y=[double]$cm.y; z=[double]$cm.z } }
  return @{ x=0.0; y=0.0; z=0.0 }
}

function Diff-Map($left,$right){
  $diffs = @()
  foreach($k in $left.Keys){
    if(-not $right.ContainsKey($k)){ continue }
    $lv = [string]$left[$k]; $rv = [string]$right[$k]
    if($lv -ne $rv){ $diffs += [pscustomobject]@{ key=$k; left=$lv; right=$rv } }
  }
  return $diffs
}

try{
  $lid = Get-Selection -Port $LeftPort
  $rid = Get-Selection -Port $RightPort

  $L = Get-InfoAndType -Port $LeftPort -ElemId $lid -TypeKeys $TypeParamKeys
  $R = Get-InfoAndType -Port $RightPort -ElemId $rid -TypeKeys $TypeParamKeys

  $le = $L.elem; $re = $R.elem
  $lt = $L.typeParams; $rt = $R.typeParams

  # 位置差
  $lc = CentroidMm $le; $rc = CentroidMm $re
  $dx = [double]$lc.x - [double]$rc.x; $dy = [double]$lc.y - [double]$rc.y; $dz = [double]$lc.z - [double]$rc.z
  $dist = [Math]::Sqrt($dx*$dx + $dy*$dy + $dz*$dz)

  # インスタンスパラメータ収集
  function Map-Params($e){
    $m = @{}
    try {
      $has = $e.PSObject.Properties.Match('parameters').Count -gt 0
      if($has){
        foreach($p in @($e.parameters)){
          try{ $n=[string]$p.name; if([string]::IsNullOrWhiteSpace($n)){ continue }; $v = $p.display; if($null -eq $v){ $v = $p.value }; $m[$n] = [string]$v } catch {}
        }
      }
    } catch {}
    return $m
  }
  $ipL = Map-Params $le
  $ipR = Map-Params $re

  # タイプパラメータ収集
  function Map-TypeParams($tp){ $m=@{}; if($tp){ if($tp.params){ foreach($k in $tp.params.PSObject.Properties.Name){ $m[$k]=[string]$tp.params.$k } }; if($tp.display){ foreach($k in $tp.display.PSObject.Properties.Name){ if(-not $m.ContainsKey($k)){ $m[$k]=[string]$tp.display.$k } } } }; return $m }
  $tpL = Map-TypeParams $lt
  $tpR = Map-TypeParams $rt

  # 比較キー
  $summaryL = @{
    elementId = [int]$le.elementId; categoryId = [int]$le.categoryId; familyName = [string]$le.familyName; typeName = [string]$le.typeName; typeId = [int]$le.typeId
  }
  $summaryR = @{
    elementId = [int]$re.elementId; categoryId = [int]$re.categoryId; familyName = [string]$re.familyName; typeName = [string]$re.typeName; typeId = [int]$re.typeId
  }

  $instDiff = Diff-Map $ipL $ipR | Where-Object { $_.key -in @('長さ','Length','Mark','符号') }
  $typeDiff = @()
  foreach($k in $TypeParamKeys){ $lv=$tpL[$k]; $rv=$tpR[$k]; if($lv -and $rv -and $lv -ne $rv){ $typeDiff += [pscustomobject]@{ key=$k; left=$lv; right=$rv } } }
  if($summaryL.familyName -ne $summaryR.familyName){ $typeDiff += [pscustomobject]@{ key='familyName'; left=$summaryL.familyName; right=$summaryR.familyName } }
  if($summaryL.typeName -ne $summaryR.typeName){ $typeDiff += [pscustomobject]@{ key='typeName'; left=$summaryL.typeName; right=$summaryR.typeName } }

  $report = [pscustomobject]@{
    ok = $true
    position = @{ left=$lc; right=$rc; distanceMm=[Math]::Round($dist,3); withinTol = ($dist -le $PosTolMm) }
    left = $summaryL
    right = $summaryR
    instanceDifferences = $instDiff
    typeDifferences = $typeDiff
  }

  $ts = Get-Date -Format 'yyyyMMdd_HHmmss'
  $outj = Join-Path (Resolve-Path (Join-Path $SCRIPT_DIR '..\\..')).Path ("Projects/selected_pair_diff_"+$ts+".json")
  $outc = Join-Path (Resolve-Path (Join-Path $SCRIPT_DIR '..\\..')).Path ("Projects/selected_pair_diff_"+$ts+".csv")
  $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $outj -Encoding UTF8
  # CSV（差分のみ）
  $rows = @()
  foreach($d in $instDiff){ $rows += [pscustomobject]@{ kind='instance'; key=$d.key; left=$d.left; right=$d.right } }
  foreach($d in $typeDiff){ $rows += [pscustomobject]@{ kind='type'; key=$d.key; left=$d.left; right=$d.right } }
  $rows | Export-Csv -LiteralPath $outc -NoTypeInformation -Encoding UTF8

  Write-Host ("Saved: JSON="+$outj+" CSV="+$outc) -ForegroundColor Green
  $report | ConvertTo-Json -Depth 8
}
catch{
  Write-Error $_
  exit 1
}






