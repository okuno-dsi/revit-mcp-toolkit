# @feature: hide elements resilient | keywords: スペース, ビュー, スナップショット
param(
  [int]$Port = 5210,
  [int[]]$ElementIds,
  [switch]$DetachViewTemplate,
  [int]$BatchSize = 800,
  [int]$MaxMillisPerTx = 3000,
  [int]$WaitSec = 240,
  [int]$JobTimeoutSec = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8 = '1'
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

$PY = Join-Path $PSScriptRoot 'send_revit_command_durable.py'

function Invoke-Mcp {
  param([string]$Method,[hashtable]$Params)
  $pjson = ($Params | ConvertTo-Json -Depth 100 -Compress)
  $args = @('--port', $Port, '--command', $Method, '--params', $pjson, '--wait-seconds', [string]$WaitSec, '--timeout-sec', [string]$JobTimeoutSec, '--force')
  $tmp = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ("mcp_"+[System.IO.Path]::GetRandomFileName()+".json"))
  $args += @('--output-file', $tmp)
  $null = & python -X utf8 $PY @args 2>$null
  $code = $LASTEXITCODE
  $txt = ''
  try { $txt = Get-Content -LiteralPath $tmp -Raw -Encoding UTF8 } catch {}
  if(Test-Path $tmp){ Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
  if($code -ne 0){ throw "MCP call failed ($Method): $txt" }
  if([string]::IsNullOrWhiteSpace($txt)){ throw "Empty response from MCP ($Method)" }
  return ($txt | ConvertFrom-Json -Depth 400)
}

function Save-ViewState { return (Invoke-Mcp 'save_view_state' @{}) }

function Hide-Resilient([int[]]$ids){
  if(-not $ids -or $ids.Count -eq 0){ throw 'No ElementIds provided.' }
  $start = 0
  $completed = $false
  $viewId = 0
  do {
    $params = @{ elementIds=@($ids); startIndex=$start; batchSize=$BatchSize; maxMillisPerTx=$MaxMillisPerTx; refreshView=$true; __smoke_ok=$true }
    if($DetachViewTemplate){ $params['detachViewTemplate'] = $true }
    $res = Invoke-Mcp 'hide_elements_in_view' $params
    foreach($p in 'result.result.viewId','result.viewId','viewId'){ try{ $cur=$res; foreach($seg in $p.Split('.')){ $cur=$cur.$seg }; if($cur){ $viewId=[int]$cur; break } }catch{} }
    $completed = $false
    foreach($p in 'result.result.completed','result.completed','completed'){ try{ $cur=$res; foreach($seg in $p.Split('.')){ $cur=$cur.$seg }; $completed=[bool]$cur; break }catch{} }
    $next = $null
    foreach($p in 'result.result.nextIndex','result.nextIndex','nextIndex'){ try{ $cur=$res; foreach($seg in $p.Split('.')){ $cur=$cur.$seg }; $next=$cur; break }catch{} }
    if(-not $completed -and $next -ne $null){ $start = [int]$next } else { break }
  } while ($true)
  return @{ viewId=$viewId; completed=$completed }
}

# Save snapshot
$snap = Save-ViewState

# Run hide in resilient batches
Hide-Resilient -ids $ElementIds | Out-Null

Write-Host 'Hide operation completed (batched).' -ForegroundColor Green


