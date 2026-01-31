# @feature: color marked diffs in compare views | keywords: スペース, ビュー
param(
  [int]$LeftPort = 5210,
  [int]$RightPort = 5211,
  [string]$BaseViewName = 'RSL1',
  [string]$CsvPath,
  [int]$R = 154,   # YellowGreen 154,205,50
  [int]$G = 205,
  [int]$B = 50,
  [int]$Transparency = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8='1'
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

$SCRIPT_DIR = $PSScriptRoot
$ROOT = (Resolve-Path (Join-Path $SCRIPT_DIR '..\\..')).Path
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'

function Call-Mcp { param([int]$Port,[string]$Method,[hashtable]$Params,[int]$W=240,[int]$T=1200,[switch]$Force)
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

function Resolve-CompareViewId([int]$Port){
  $names = @("$BaseViewName 相違", "$BaseViewName 相違 *")
  $lov = Payload (Call-Mcp $Port 'list_open_views' @{} 60 120 -Force)
  $vs = @($lov.views)
  $vid = 0
  foreach($n in $names){ $v = $vs | Where-Object { $_.name -like $n } | Select-Object -First 1; if($v){ $vid=[int]$v.viewId; break } }
  if($vid -le 0){
    # Try to open existing view by name
    try { $null = Call-Mcp $Port 'open_views' @{ names=@("$BaseViewName 相違") } 60 120 -Force } catch {}
    $lov2 = Payload (Call-Mcp $Port 'list_open_views' @{} 60 120 -Force); $v2 = @($lov2.views) | Where-Object { $_.name -eq ("$BaseViewName 相違") } | Select-Object -First 1; if($v2){ $vid=[int]$v2.viewId }
  }
  if($vid -le 0){
    # Duplicate from base view
    try {
      $lov3 = Payload (Call-Mcp $Port 'list_open_views' @{} 60 120 -Force)
      $base = @($lov3.views) | Where-Object { $_.name -eq $BaseViewName } | Select-Object -First 1
      if($base){
        $dup = Payload (Call-Mcp $Port 'duplicate_view' @{ viewId=[int]$base.viewId; withDetailing=$true; desiredName=($BaseViewName+' 相違'); onNameConflict='returnExisting'; idempotencyKey=("dup:{0}:{1}" -f $base.viewId,($BaseViewName+' 相違')) } 180 360 -Force)
        try { $vid = [int]$dup.viewId } catch { try { $vid = [int]$dup.newViewId } catch { $vid = 0 } }
      }
    } catch {}
  }
  if($vid -le 0){ throw "Port ${Port}: 相違ビューが見つかりません（'$BaseViewName 相違'）" }
  try { $null = Call-Mcp $Port 'activate_view' @{ viewId=$vid } 60 120 -Force } catch {}
  return $vid
}

if([string]::IsNullOrWhiteSpace($CsvPath)){
  $CsvPath = (Get-ChildItem Work -File -Filter 'diffs_or_type_or_distance_or_presence_*.csv' | Sort-Object LastWriteTime | Select-Object -Last 1).FullName
}
if(-not (Test-Path -LiteralPath $CsvPath)){ throw "CSV not found: $CsvPath" }

# Read rows and collect ids per port
$rows = @()
try {
  $rowsCsv = Import-Csv -LiteralPath $CsvPath -Encoding UTF8
  foreach($row in $rowsCsv){ $rows += $row }
} catch { throw "Failed to read CSV: $CsvPath" }

$idsL = New-Object System.Collections.Generic.HashSet[int]
$idsR = New-Object System.Collections.Generic.HashSet[int]
foreach($row2 in $rows){
  try{
    $port = [int]("$($row2.'port')")
    $id   = [int]("$($row2.'elementId')")
    if($port -eq $LeftPort){ [void]$idsL.Add($id) }
    elseif($port -eq $RightPort){ [void]$idsR.Add($id) }
  } catch{}
}

$Lview = Resolve-CompareViewId -Port $LeftPort
$Rview = Resolve-CompareViewId -Port $RightPort

try { $null = Call-Mcp $LeftPort 'set_view_template' @{ viewId=$Lview; clear=$true } 60 120 -Force } catch {}
try { $null = Call-Mcp $RightPort 'set_view_template' @{ viewId=$Rview; clear=$true } 60 120 -Force } catch {}

# Reset temporary hide/isolate and ensure structural categories are visible
try { $null = Call-Mcp $LeftPort  'show_all_in_view' @{ viewId=$Lview; detachViewTemplate=$true; includeTempReset=$true; unhideElements=$true; clearElementOverrides=$false; batchSize=2000; startIndex=0; refreshView=$true } 180 480 -Force } catch {}
try { $null = Call-Mcp $RightPort 'show_all_in_view' @{ viewId=$Rview; detachViewTemplate=$true; includeTempReset=$true; unhideElements=$true; clearElementOverrides=$false; batchSize=2000; startIndex=0; refreshView=$true } 180 480 -Force } catch {}
try { $null = Call-Mcp $LeftPort  'set_category_visibility' @{ viewId=$Lview; categoryIds=@(-2001320,-2001330); visible=$true } 60 120 -Force } catch {}
try { $null = Call-Mcp $RightPort 'set_category_visibility' @{ viewId=$Rview; categoryIds=@(-2001320,-2001330); visible=$true } 60 120 -Force } catch {}
try { $null = Call-Mcp $LeftPort  'view_fit' @{ viewId=$Lview } 30 60 -Force } catch {}
try { $null = Call-Mcp $RightPort 'view_fit' @{ viewId=$Rview } 30 60 -Force } catch {}

# Sync view state from base view (RSL1) to compare views for stability
try {
  $baseL = (Payload (Call-Mcp $LeftPort 'list_open_views' @{} 60 120 -Force)).views | Where-Object { $_.name -eq $BaseViewName } | Select-Object -First 1
  if($baseL){ $null = Call-Mcp $LeftPort 'sync_view_state' @{ srcViewId=[int]$baseL.viewId; dstViewId=[int]$Lview } 120 240 -Force }
} catch {}
try {
  $baseR = (Payload (Call-Mcp $RightPort 'list_open_views' @{} 60 120 -Force)).views | Where-Object { $_.name -eq $BaseViewName } | Select-Object -First 1
  if($baseR){ $null = Call-Mcp $RightPort 'sync_view_state' @{ srcViewId=[int]$baseR.viewId; dstViewId=[int]$Rview } 120 240 -Force }
} catch {}

function Apply-Color([int]$Port,[int]$ViewId,[int[]]$Ids){
  $batch = 200
  for($i=0; $i -lt $Ids.Count; $i+=$batch){
    $chunk = @($Ids[$i..([Math]::Min($i+$batch-1,$Ids.Count-1))])
    $params = @{ viewId=$ViewId; elementIds=$chunk; r=$R; g=$G; b=$B; transparency=$Transparency }
    try { $null = Call-Mcp $Port 'set_visual_override' $params 240 1200 -Force } catch {}
  }
}

Apply-Color -Port $LeftPort -ViewId $Lview -Ids (@($idsL.GetEnumerator() | ForEach-Object { [int]$_ }))
Apply-Color -Port $RightPort -ViewId $Rview -Ids (@($idsR.GetEnumerator() | ForEach-Object { [int]$_ }))

try {
  $voL = Payload (Call-Mcp $LeftPort 'get_visual_overrides_in_view' @{ viewId=$Lview } 60 240 -Force)
  $voR = Payload (Call-Mcp $RightPort 'get_visual_overrides_in_view' @{ viewId=$Rview } 60 240 -Force)
  $cntL = 0; $cntR = 0
  try { $cntL = @($voL.overrides).Count } catch {}
  try { $cntR = @($voR.overrides).Count } catch {}
  Write-Host ("Colored elements. LeftIds="+$idsL.Count+" RightIds="+$idsR.Count+" | Overrides L="+$cntL+" R="+$cntR+" CSV="+$CsvPath) -ForegroundColor Green
} catch {
  Write-Host ("Colored elements. LeftIds="+$idsL.Count+" RightIds="+$idsR.Count+" CSV="+$CsvPath) -ForegroundColor Green
}
