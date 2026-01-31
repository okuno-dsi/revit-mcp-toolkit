# @feature: unhide current selection in view | keywords: スペース, ビュー
param(
  [int]$Port = 5210,
  [int]$BatchSize = 800,
  [int]$MaxMillisPerTx = 3000,
  [int]$WaitSec = 180,
  [int]$JobTimeoutSec = 240
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8 = '1'

$PY = Join-Path $PSScriptRoot 'send_revit_command_durable.py'
if(!(Test-Path $PY)) { Write-Error "Python client not found: $PY"; exit 2 }

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
  try { $lvl1 = $obj | Select-Object -ExpandProperty result -ErrorAction Stop } catch { return $obj }
  try { $lvl2 = $lvl1 | Select-Object -ExpandProperty result -ErrorAction Stop; return $lvl2 } catch { return $lvl1 }
}

# Active view
$cv = Call-Mcp 'get_current_view' @{} 60 120 -Force
$cvp = Get-JsonPayload $cv
$viewId = 0; try { $viewId = [int]$cvp.viewId } catch {}
if($viewId -le 0){ Write-Error 'Could not resolve active viewId.'; exit 2 }
Write-Host ("[View] Active viewId={0}" -f $viewId) -ForegroundColor Cyan

# Current selection
$sel = Call-Mcp 'get_selected_element_ids' @{} 60 120 -Force
$sp = Get-JsonPayload $sel
$ids = @(); try { $ids = @($sp.elementIds | ForEach-Object { [int]$_ }) } catch {}
if($ids.Count -eq 0){ Write-Error 'No elements selected in Revit.'; exit 3 }
Write-Host ("[Selection] elementIds count={0}" -f $ids.Count) -ForegroundColor Gray

# Unhide selection in active view (batched)
$start = 0
while($true){
  $r = Call-Mcp 'unhide_elements_in_view' @{ viewId=$viewId; elementIds=@($ids); detachViewTemplate=$true; batchSize=$BatchSize; maxMillisPerTx=$MaxMillisPerTx; startIndex=$start; refreshView=$true; __smoke_ok=$true } 180 240 -Force
  $rp = Get-JsonPayload $r
  $completed = $false; $next = $null
  foreach($p in 'completed','result.completed','result.result.completed'){ try { $cur=$rp; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $completed=[bool]$cur; break } catch {} }
  foreach($p in 'nextIndex','result.nextIndex','result.result.nextIndex'){ try { $cur=$rp; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $next=$cur; break } catch {} }
  if(-not $completed -and $null -ne $next){ try { $start=[int]$next } catch { $start=0 } } else { break }
}

Write-Host ("[Done] Unhid selected elements in viewId={0}. Section box unchanged." -f $viewId) -ForegroundColor Green
