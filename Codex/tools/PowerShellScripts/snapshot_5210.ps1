# @feature: snapshot 5210 | keywords: 柱, ビュー, スナップショット
param(
  [int]$WaitSec = 240,
  [int]$JobTimeoutSec = 480
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try { chcp 65001 > $null } catch {}
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

$Port = 5210
$PY = Join-Path $PSScriptRoot 'send_revit_command_durable.py'

function Invoke-Mcp {
  param([string]$Method,[hashtable]$Params,[int]$Wait=$WaitSec,[int]$JobSec=$JobTimeoutSec)
  $pjson = ($Params | ConvertTo-Json -Depth 100 -Compress)
  $tmp = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ("mcp_"+[System.IO.Path]::GetRandomFileName()+".json"))
  $args = @('--port', $Port, '--command', $Method, '--params', $pjson, '--wait-seconds', [string]$Wait, '--timeout-sec', [string]$JobSec, '--output-file', $tmp)
  $null = & python -X utf8 $PY @args 2>$null
  $code = $LASTEXITCODE
  $txt = ''
  try { $txt = Get-Content -LiteralPath $tmp -Raw -Encoding UTF8 } catch {}
  if(Test-Path $tmp){ Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
  if($code -ne 0){ throw "MCP call failed ($Method)" }
  return ($txt | ConvertFrom-Json -Depth 100)
}

# Run directory
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$work = Join-Path $root 'Work'
if(!(Test-Path $work)){ New-Item -ItemType Directory -Path $work | Out-Null }
$stamp = Get-Date -Format yyyyMMdd_HHmmss
$run = Join-Path $work ("SnapshotRun_5210_"+$stamp)
New-Item -ItemType Directory -Path $run | Out-Null

# Ensure RSL1 active
$null = Invoke-Mcp 'open_views' @{ names=@('RSL1') } 120 240
$cv = Invoke-Mcp 'get_current_view' @{} 60 120
$vid = [int]$cv.result.result.viewId
if($vid -le 0){ throw 'Could not resolve RSL1 viewId on 5210' }

# Snapshot (structural framing/columns with analytic wire)
$out = Join-Path $run 'snap_5210_RSL1.json'
$snap = Invoke-Mcp 'snapshot_view_elements' @{ viewId=$vid; categoryIds=@(-2001320,-2001330); includeAnalytic=$true; includeHidden=$false } 240 480
$snap | ConvertTo-Json -Depth 100 | Set-Content -Encoding UTF8 $out

# Summary to console
$count = 0; try { $count = [int]$snap.result.result.count } catch {}
Write-Output ("RUN="+$run)
Write-Output ("saved="+$out)
Write-Output ("count="+$count)


