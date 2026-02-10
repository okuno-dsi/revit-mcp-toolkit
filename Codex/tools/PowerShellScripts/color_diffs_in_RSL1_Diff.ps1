# @feature: color diffs in RSL1 Diff | keywords: スペース, ビュー
param(
  [int]$LeftPort = 5210,
  [int]$RightPort = 5211,
  [string]$BaseViewName = 'RSL1',
  [string]$CompareViewName = 'RSL1 Diff',
  [string]$CsvPath,
  [int]$R = 154,   # YellowGreen 154,205,50
  [int]$G = 205,
  [int]$B = 50,
  [int]$Transparency = 0
)

Set-StrictMode -Version Latest
${ErrorActionPreference} = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8 = '1'
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

$SCRIPT_DIR = $PSScriptRoot
$ROOT = (Resolve-Path (Join-Path $SCRIPT_DIR '..\..')).Path
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'

function Call-Mcp {
  param(
    [int]$Port,
    [string]$Method,
    [hashtable]$Params,
    [int]$W = 240,
    [int]$T = 1200,
    [switch]$Force
  )
  $pjson = ($Params | ConvertTo-Json -Depth 120 -Compress)
  $args = @('--port', $Port, '--command', $Method, '--params', $pjson, '--wait-seconds', [string]$W, '--timeout-sec', [string]$T)
  if ($Force) { $args += '--force' }
  $tmp = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ('mcp_' + [System.IO.Path]::GetRandomFileName() + '.json'))
  $args += @('--output-file', $tmp)
  $null = & python -X utf8 $PY @args 2>$null
  $code = $LASTEXITCODE
  $txt = ''
  try { $txt = Get-Content -LiteralPath $tmp -Raw -Encoding UTF8 } catch {}
  if (Test-Path $tmp) { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
  if ($code -ne 0) { throw "MCP failed ($Method): $txt" }
  if ([string]::IsNullOrWhiteSpace($txt)) { throw "Empty MCP response ($Method)" }
  return ($txt | ConvertFrom-Json -Depth 400)
}

function Payload($obj) {
  if ($obj.result -and $obj.result.result) { return $obj.result.result }
  elseif ($obj.result) { return $obj.result }
  else { return $obj }
}

function Resolve-Or-DuplicateViewId {
  param([int]$Port, [string]$BaseName, [string]$CompareName)
  # Try find existing compare view
  try {
    $lov = Payload (Call-Mcp $Port 'list_open_views' @{} 60 120 -Force)
    $vs = @($lov.views)
    $target = $vs | Where-Object { ([string]$_.name) -eq $CompareName } | Select-Object -First 1
    if ($target) {
      try { $null = Call-Mcp $Port 'activate_view' @{ viewId = [int]$target.viewId } 30 60 -Force } catch {}
      return [int]$target.viewId
    }
  } catch {}
  # Try open by name
  try {
    $null = Call-Mcp $Port 'open_views' @{ names = @($CompareName) } 60 120 -Force
    $lov2 = Payload (Call-Mcp $Port 'list_open_views' @{} 60 120 -Force)
    $v2 = @($lov2.views) | Where-Object { ([string]$_.name) -eq $CompareName } | Select-Object -First 1
    if ($v2) {
      return [int]$v2.viewId
    }
  } catch {}

  # Duplicate from base view with desired name
  $baseVid = 0
  try {
    $lov3 = Payload (Call-Mcp $Port 'list_open_views' @{} 60 120 -Force)
    $base = @($lov3.views) | Where-Object { ([string]$_.name) -eq $BaseName } | Select-Object -First 1
    if ($base) { $baseVid = [int]$base.viewId }
  } catch {}
  if ($baseVid -le 0) {
    try { $null = Call-Mcp $Port 'open_views' @{ names = @($BaseName) } 60 120 -Force } catch {}
    try {
      $lov4 = Payload (Call-Mcp $Port 'list_open_views' @{} 60 120 -Force)
      $base2 = @($lov4.views) | Where-Object { ([string]$_.name) -eq $BaseName } | Select-Object -First 1
      if ($base2) { $baseVid = [int]$base2.viewId }
    } catch {}
  }
  if ($baseVid -le 0) {
    # Fallback: search all views by name (exact or contains)
    try {
      $gv = Payload (Call-Mcp $Port 'get_views' @{ includeTemplates = $false; detail = $false; nameContains = $BaseName } 120 300 -Force)
      $views = @($gv.views)
      $cand = $views | Where-Object { ([string]$_.name) -eq $BaseName } | Select-Object -First 1
      if (-not $cand) { $cand = $views | Where-Object { ([string]$_.name) -like ("*"+$BaseName+"*") } | Select-Object -First 1 }
      if ($cand) { $baseVid = [int]$cand.viewId }
    } catch {}
  }
  if ($baseVid -le 0) { throw "Port ${Port}: base view not found: '$BaseName'" }

  $idem = ("dup:{0}:{1}" -f $baseVid, $CompareName)
  $dup = Payload (Call-Mcp $Port 'duplicate_view' @{ viewId = $baseVid; withDetailing = $true; desiredName = $CompareName; onNameConflict = 'returnExisting'; idempotencyKey = $idem } 180 360 -Force)
  $newVid = 0; try { $newVid = [int]$dup.viewId } catch { try { $newVid = [int]$dup.newViewId } catch { $newVid = 0 } }
  if ($newVid -le 0) { throw "duplicate_view did not return viewId (port=$Port)" }
  try { $null = Call-Mcp $Port 'activate_view' @{ viewId = $newVid } 30 60 -Force } catch {}
  return $newVid
}

if ([string]::IsNullOrWhiteSpace($CsvPath)) {
  $cand = Get-ChildItem (Join-Path $ROOT 'Work') -File -Filter 'diffs_or_type_or_distance_or_presence_*.csv' | Sort-Object LastWriteTime | Select-Object -Last 1
  if (-not $cand) { throw 'Diff CSV not found. Please specify -CsvPath.' }
  $CsvPath = $cand.FullName
}
if (-not (Test-Path -LiteralPath $CsvPath)) { throw "CSV not found: $CsvPath" }

# Collect ids per port
$rowsCsv = Import-Csv -LiteralPath $CsvPath -Encoding UTF8
$idsL = New-Object System.Collections.Generic.HashSet[int]
$idsR = New-Object System.Collections.Generic.HashSet[int]
foreach ($row in $rowsCsv) {
  try {
    $port = [int]("$($row.port)")
    $eid  = [int]("$($row.elementId)")
    if ($eid -le 0) { continue }
    if ($port -eq $LeftPort) { [void]$idsL.Add($eid) }
    elseif ($port -eq $RightPort) { [void]$idsR.Add($eid) }
  } catch {}
}

# Resolve or create compare views named "$CompareViewName"
$Lview = Resolve-Or-DuplicateViewId -Port $LeftPort -BaseName $BaseViewName -CompareName $CompareViewName
$Rview = Resolve-Or-DuplicateViewId -Port $RightPort -BaseName $BaseViewName -CompareName $CompareViewName

# Clear view template and prep
try { $null = Call-Mcp $LeftPort 'set_view_template' @{ viewId = $Lview; clear = $true } 60 120 -Force } catch {}
try { $null = Call-Mcp $RightPort 'set_view_template' @{ viewId = $Rview; clear = $true } 60 120 -Force } catch {}
try { $null = Call-Mcp $LeftPort  'show_all_in_view' @{ viewId = $Lview; detachViewTemplate=$true; includeTempReset=$true; unhideElements=$true; clearElementOverrides=$false; batchSize=2000; startIndex=0; refreshView=$true } 180 480 -Force } catch {}
try { $null = Call-Mcp $RightPort 'show_all_in_view' @{ viewId = $Rview; detachViewTemplate=$true; includeTempReset=$true; unhideElements=$true; clearElementOverrides=$false; batchSize=2000; startIndex=0; refreshView=$true } 180 480 -Force } catch {}
try { $null = Call-Mcp $LeftPort  'set_category_visibility' @{ viewId = $Lview; categoryIds=@(-2001320,-2001330); visible=$true } 60 120 -Force } catch {}
try { $null = Call-Mcp $RightPort 'set_category_visibility' @{ viewId = $Rview; categoryIds=@(-2001320,-2001330); visible=$true } 60 120 -Force } catch {}
try {
  $baseL = (Payload (Call-Mcp $LeftPort 'list_open_views' @{} 60 120 -Force)).views | Where-Object { $_.name -eq $BaseViewName } | Select-Object -First 1
  if ($baseL) { $null = Call-Mcp $LeftPort 'sync_view_state' @{ srcViewId=[int]$baseL.viewId; dstViewId=$Lview } 120 240 -Force }
} catch {}
try {
  $baseR = (Payload (Call-Mcp $RightPort 'list_open_views' @{} 60 120 -Force)).views | Where-Object { $_.name -eq $BaseViewName } | Select-Object -First 1
  if ($baseR) { $null = Call-Mcp $RightPort 'sync_view_state' @{ srcViewId=[int]$baseR.viewId; dstViewId=$Rview } 120 240 -Force }
} catch {}
try { $null = Call-Mcp $LeftPort  'view_fit' @{ viewId = $Lview } 30 60 -Force } catch {}
try { $null = Call-Mcp $RightPort 'view_fit' @{ viewId = $Rview } 30 60 -Force } catch {}

function Apply-Color([int]$Port, [int]$ViewId, [int[]]$Ids) {
  if (-not $Ids -or $Ids.Count -eq 0) { return }
  $batch = 200
  for ($i = 0; $i -lt $Ids.Count; $i += $batch) {
    $chunk = @($Ids[$i..([Math]::Min($i + $batch - 1, $Ids.Count - 1))])
    $params = @{ viewId = $ViewId; elementIds = $chunk; r = $R; g = $G; b = $B; transparency = $Transparency }
    try { $null = Call-Mcp $Port 'set_visual_override' $params 240 1200 -Force } catch {}
  }
}

$LeftIdsArr  = @($idsL.GetEnumerator() | ForEach-Object { [int]$_ })
$RightIdsArr = @($idsR.GetEnumerator() | ForEach-Object { [int]$_ })

Apply-Color -Port $LeftPort -ViewId $Lview -Ids $LeftIdsArr
Apply-Color -Port $RightPort -ViewId $Rview -Ids $RightIdsArr

try {
  $voL = Payload (Call-Mcp $LeftPort 'get_visual_overrides_in_view' @{ viewId = $Lview } 60 240 -Force)
  $voR = Payload (Call-Mcp $RightPort 'get_visual_overrides_in_view' @{ viewId = $Rview } 60 240 -Force)
  $cntL = 0; $cntR = 0
  try { $cntL = @($voL.overrides).Count } catch {}
  try { $cntR = @($voR.overrides).Count } catch {}
  Write-Host ("Colored elements. LeftIds=" + $LeftIdsArr.Count + " RightIds=" + $RightIdsArr.Count + " | Overrides L=" + $cntL + " R=" + $cntR + " CSV=" + $CsvPath) -ForegroundColor Green
} catch {
  Write-Host ("Colored elements. LeftIds=" + $LeftIdsArr.Count + " RightIds=" + $RightIdsArr.Count + " CSV=" + $CsvPath) -ForegroundColor Green
}

