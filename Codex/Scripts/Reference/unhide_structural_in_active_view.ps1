param(
  [int]$Port = 5210,
  [int]$BatchSize = 800,
  [int]$MaxMillisPerTx = 3000,
  [int]$WaitSec = 360,
  [int]$JobTimeoutSec = 360
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8 = '1'
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

$PY = Join-Path $PSScriptRoot 'send_revit_command_durable.py'
if(!(Test-Path $PY)) { Write-Error "Python client not found: $PY"; exit 2 }

$workRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\Work')
$projDir = Join-Path $workRoot ("Unhide_Structural_{0}" -f $Port)
if(!(Test-Path $projDir)){ [void](New-Item -ItemType Directory -Path $projDir) }
$logs = Join-Path $projDir 'Logs'
if(!(Test-Path $logs)){ [void](New-Item -ItemType Directory -Path $logs) }

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

function Get-JsonPayload($obj){
  try {
    $level1 = $obj | Select-Object -ExpandProperty result -ErrorAction Stop
  } catch { return $obj }
  try {
    $level2 = $level1 | Select-Object -ExpandProperty result -ErrorAction Stop
    return $level2
  } catch { return $level1 }
}

# Resolve active view id
$cv = Call-Mcp 'get_current_view' @{} 60 120 -Force
$cvp = Get-JsonPayload $cv
$viewId = 0; try { $viewId = [int]$cvp.viewId } catch {}
if($viewId -le 0){ Write-Error 'Could not resolve active viewId.'; exit 2 }
Write-Host ("[View] Active viewId={0}" -f $viewId) -ForegroundColor Cyan

# Gather all Structural Framing elementIds
$frames = Call-Mcp 'get_structural_frames' @{} 240 360 -Force
$fp = Get-JsonPayload $frames
$frameIds = @()
try { $frameIds = @($fp.structuralFrames | ForEach-Object { [int]$_.elementId } | Sort-Object -Unique) } catch {}
Write-Host ("[Collect] Structural Framing total={0}" -f $frameIds.Count) -ForegroundColor Gray

# Gather all Structural Columns elementIds
$cols = Call-Mcp 'get_structural_columns' @{} 240 360 -Force
$cp = Get-JsonPayload $cols
$colIds = @()
try {
  $colIds = @($cp.structuralColumns | ForEach-Object { [int]$_.elementId } | Sort-Object -Unique)
} catch {}
Write-Host ("[Collect] Structural Columns total={0}" -f $colIds.Count) -ForegroundColor Gray

$targets = @($frameIds + $colIds | Sort-Object -Unique)
if($targets.Count -eq 0){ Write-Host '[Info] No structural frames/columns found.' -ForegroundColor Yellow; exit 0 }

# Unhide in active view (batched)
$start = 0
while($true){
  $r = Call-Mcp 'unhide_elements_in_view' @{ viewId=$viewId; elementIds=@($targets); detachViewTemplate=$true; batchSize=$BatchSize; maxMillisPerTx=$MaxMillisPerTx; startIndex=$start; refreshView=$true; __smoke_ok=$true } 300 360 -Force
  $rp = Get-JsonPayload $r
  $completed = $false; $next = $null
  foreach($p in 'completed','result.completed','result.result.completed'){ try { $cur=$rp; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $completed=[bool]$cur; break } catch {} }
  foreach($p in 'nextIndex','result.nextIndex','result.result.nextIndex'){ try { $cur=$rp; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $next=$cur; break } catch {} }
  if(-not $completed -and $null -ne $next){ try { $start=[int]$next } catch { $start=0 } } else { break }
}

Write-Host ("[Done] Unhid {0} structural elements in viewId={1}. Section box kept as-is." -f $targets.Count, $viewId) -ForegroundColor Green
