param(
  [int]$Port = 5210,
  [string]$ViewNamePrefix = 'Focus3D_Selected_',
  [int]$BatchSize = 800,
  [int]$MaxMillisPerTx = 3000,
  [int]$WaitSec = 360,
  [int]$JobTimeoutSec = 360,
  [int]$PaddingMm = 200,
  # Deprecated: previously used to skip visibility reset. Default behavior is now to SKIP reset.
  [switch]$SkipShowAll,
  # New: opt-in to run visibility reset (detach template + clear overrides + unhide). Default is OFF (skip).
  [switch]$ShowAll,
  # New: opt-in to hide non-selected elements (default: do NOT hide)
  [switch]$HideNonSelected
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8 = '1'
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

$PY = Join-Path $PSScriptRoot 'send_revit_command_durable.py'
if(!(Test-Path $PY)) { Write-Error "Python client not found: $PY"; exit 2 }

$workRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\Work')
$projDir = Join-Path $workRoot ("Focus3D_Selected_{0}" -f $Port)
if(!(Test-Path $projDir)){ [void](New-Item -ItemType Directory -Path $projDir) }
$logs = Join-Path $projDir 'Logs'
if(!(Test-Path $logs)){ [void](New-Item -ItemType Directory -Path $logs) }

function Get-JsonPayload($obj){ if($obj.result){ if($obj.result.result){ return $obj.result.result } return $obj.result } return $obj }

function Call-Mcp {
  param([string]$Method,[hashtable]$Params,[int]$Wait=$WaitSec,[int]$JobSec=$JobTimeoutSec,[switch]$Force)
  if(-not $Params){ $Params = @{} }
  $pjson = ($Params | ConvertTo-Json -Depth 100 -Compress)
  $tmp = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ("mcp_"+[System.IO.Path]::GetRandomFileName()+".json"))
  $args = @('--port', $Port, '--command', $Method, '--params', $pjson, '--wait-seconds', [string]$Wait, '--output-file', $tmp)
  if($JobSec -gt 0){ $args += @('--timeout-sec', [string]$JobSec) }
  if($Force){ $args += '--force' }
  $null = & python -X utf8 $PY @args 2>$null
  $code = $LASTEXITCODE
  $txt = ''
  try { $txt = Get-Content -LiteralPath $tmp -Raw -Encoding UTF8 } catch {}
  if(Test-Path $tmp){ Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
  if($code -ne 0){ throw "MCP call failed ($Method): $txt" }
  if([string]::IsNullOrWhiteSpace($txt)){ throw "Empty response from MCP ($Method)" }
  return ($txt | ConvertFrom-Json -Depth 400)
}

# 1) Selected element ids
$sel = Call-Mcp 'get_selected_element_ids' @{} 60 120 -Force
$s = Get-JsonPayload $sel
$ids = @(); try { $ids = @($s.elementIds | ForEach-Object { [int]$_ }) } catch {}
if($ids.Count -eq 0){ Write-Error 'No elements selected in Revit.'; exit 3 }
Write-Host ("[Selection] Count={0}" -f $ids.Count) -ForegroundColor Cyan

# 2) Create a 3D view and activate
$name = ($ViewNamePrefix + (Get-Date -Format 'yyyyMMdd_HHmmss'))
$cv = Call-Mcp 'create_3d_view' @{ name=$name; __smoke_ok=$true } 120 240 -Force
$cvp = Get-JsonPayload $cv
$viewId = 0; try { $viewId = [int]$cvp.viewId } catch {}
if($viewId -le 0){ Write-Error 'create_3d_view did not return a viewId.'; exit 2 }
Write-Host ("[View] Created 3D viewId={0} name='{1}'" -f $viewId, $name) -ForegroundColor Cyan

[void](Call-Mcp 'activate_view' @{ viewId=$viewId } 60 120 -Force)
Write-Host "[View] Activated" -ForegroundColor Gray

# 3) Optionally reset visibility (detach template, clear temp)
# Default behavior: SKIP visibility reset unless explicitly requested via -ShowAll
$doShowAll = $false
if ($PSBoundParameters.ContainsKey('ShowAll') -and $ShowAll.IsPresent) {
  $doShowAll = $true
} elseif ($PSBoundParameters.ContainsKey('SkipShowAll') -and $SkipShowAll.IsPresent) {
  # Back-compat: explicit -SkipShowAll keeps skipping
  $doShowAll = $false
} else {
  # No flags provided: skip by default
  $doShowAll = $false
}

if($doShowAll){
  $idx = 0
  do {
    $r = Call-Mcp 'show_all_in_view' @{ viewId=$viewId; detachViewTemplate=$true; includeTempReset=$true; unhideElements=$true; clearElementOverrides=$true; batchSize=$BatchSize; startIndex=$idx; refreshView=$true; __smoke_ok=$true } 180 240 -Force
    $rp = Get-JsonPayload $r
    $next = $null
    foreach($p in 'nextIndex','result.nextIndex','result.result.nextIndex'){ try { $cur=$rp; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $next=$cur; break } catch {} }
    if($null -ne $next){ try { $idx=[int]$next } catch { $idx=0 } } else { $idx=0 }
  } while($idx -gt 0)
  Write-Host "[View] Reset visibility and overrides" -ForegroundColor Gray
} else {
  Write-Host "[Skip] Reset visibility and overrides (default)" -ForegroundColor Gray
}

# 4) (Optional) Hide non-selected elements in the new 3D view
if ($HideNonSelected.IsPresent) {
  $all = Call-Mcp 'get_elements_in_view' @{ viewId=$viewId; _shape=@{ idsOnly=$true; page=@{ limit=200000 } } } 240 360 -Force
  $ap = Get-JsonPayload $all
  $allIds = @(); try { $allIds = @($ap.elementIds | ForEach-Object { [int]$_ }) } catch {}
  if($allIds.Count -gt 0){
    $selSet = New-Object 'System.Collections.Generic.HashSet[int]'
    foreach($i in $ids){ [void]$selSet.Add([int]$i) }
    $toHide = New-Object System.Collections.Generic.List[Int32]
    foreach($i in $allIds){ if(-not $selSet.Contains([int]$i)){ [void]$toHide.Add([int]$i) } }
    Write-Host ("[Hide] Non-selected count={0}" -f $toHide.Count) -ForegroundColor Gray
    if($toHide.Count -gt 0){
      $start = 0
      while($true){
        $r = Call-Mcp 'hide_elements_in_view' @{ viewId=$viewId; elementIds=@($toHide); detachViewTemplate=$true; batchSize=$BatchSize; maxMillisPerTx=$MaxMillisPerTx; startIndex=$start; refreshView=$true; __smoke_ok=$true } 300 360 -Force
        $rp = Get-JsonPayload $r
        $completed = $false; $next = $null
        foreach($p in 'completed','result.completed','result.result.completed'){ try { $cur=$rp; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $completed=[bool]$cur; break } catch {} }
        foreach($p in 'nextIndex','result.nextIndex','result.result.nextIndex'){ try { $cur=$rp; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $next=$cur; break } catch {} }
        if(-not $completed -and $null -ne $next){ try { $start=[int]$next } catch { $start=0 } } else { break }
      }
    }
  }
} else {
  Write-Host "[Skip] Hiding non-selected elements (default behavior)" -ForegroundColor Gray
}

# 5) Tight section box around the selection
[void](Call-Mcp 'set_section_box_by_elements' @{ viewId=$viewId; elementIds=@($ids); paddingMm=$PaddingMm; detachViewTemplate=$true; __smoke_ok=$true } 240 360 -Force)

# 6) Fit the view
try { [void](Call-Mcp 'view_fit' @{ viewId=$viewId } 60 120 -Force) } catch {}

Write-Host ("[Done] Focus 3D view ready. viewId={0} name='{1}'" -f $viewId, $name) -ForegroundColor Green
