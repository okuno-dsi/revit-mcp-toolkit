# @feature: repair active view visibility | keywords: スペース, ビュー
param(
  [int]$Port = 5210,
  [int]$BatchSize = 800,
  [int]$MaxMillisPerTx = 3000,
  [int]$WaitSec = 240,
  [int]$JobTimeoutSec = 360
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

function Get-JsonPayload($obj){ if($obj.result){ if($obj.result.result){ return $obj.result.result } return $obj.result } return $obj }

$cv = Call-Mcp 'get_current_view' @{} 60 120 -Force
$cvp = Get-JsonPayload $cv
$viewId = 0; try { $viewId = [int]$cvp.viewId } catch {}
if($viewId -le 0){ Write-Error 'Could not resolve active viewId.'; exit 2 }
Write-Host ("[View] Active viewId={0}" -f $viewId) -ForegroundColor Cyan

Write-Host "[Reset] Detach template, unhide, clear overrides (batched)..." -ForegroundColor Yellow
$start = 0
do {
  $r = Call-Mcp 'show_all_in_view' @{ viewId=$viewId; detachViewTemplate=$true; includeTempReset=$true; unhideElements=$true; clearElementOverrides=$true; batchSize=$BatchSize; maxMillisPerTx=$MaxMillisPerTx; startIndex=$start; refreshView=$true; __smoke_ok=$true } 180 240 -Force
  $rp = Get-JsonPayload $r
  $next = $null
  foreach($p in 'nextIndex','result.nextIndex','result.result.nextIndex'){ try { $cur=$rp; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; $next=$cur; break } catch {} }
  if($null -ne $next){ try { $start=[int]$next } catch { $start = 0 } } else { $start = 0 }
} while ($start -gt 0)

Write-Host "[Done] View visibility reset completed." -ForegroundColor Green


