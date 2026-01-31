# @feature: delete revision clouds by viewname | keywords: スペース, ビュー
param(
  [Parameter(Mandatory=$true)][int]$Port,
  [string]$NameLike = '*相違*',
  [switch]$IncludeActiveView,
  [int]$WaitSec = 180,
  [int]$JobTimeoutSec = 600
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8='1'
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

$PY = Join-Path $PSScriptRoot 'send_revit_command_durable.py'

function Invoke-Mcp {
  param([string]$Method,[hashtable]$Params,[int]$Wait=$WaitSec,[int]$JobSec=$JobTimeoutSec,[switch]$Force)
  $pjson = ($Params | ConvertTo-Json -Depth 100 -Compress)
  $args = @('--port', $Port, '--command', $Method, '--params', $pjson, '--wait-seconds', [string]$Wait)
  if($JobSec -gt 0){ $args += @('--timeout-sec', [string]$JobSec) }
  if($Force){ $args += '--force' }
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

function Get-Result($obj){ if($obj.result -and $obj.result.result){ return $obj.result.result } elseif($obj.result){ return $obj.result } else { return $obj } }

# Gather clouds across all revisions
$lr = Get-Result (Invoke-Mcp 'list_revisions' @{ includeClouds = $true; cloudFields = @('elementId','viewId') } 120 300 -Force)
$revItems = @(); try { $revItems = @($lr.revisions) } catch {}
$clouds = @(); foreach($rv in $revItems){ if($rv.clouds){ $clouds += $rv.clouds } }

# Resolve view names for viewIds present in clouds
$vidsCandidate = @($clouds | ForEach-Object { try { [int]$_.viewId } catch { $null } } | Where-Object { $_ -ne $null })
if($vidsCandidate.Count -gt 0){
  $viewIds = @($vidsCandidate | Sort-Object -Unique)
} else {
  $viewIds = @()
}
$nameMap = @{}
foreach($vid in $viewIds){
  try {
    $vi = Get-Result (Invoke-Mcp 'get_view_info' @{ viewId = [int]$vid } 60 180 -Force)
    $nm = [string]$vi.view.name; if([string]::IsNullOrWhiteSpace($nm)){ $nm = [string]$vi.name }
    if(-not [string]::IsNullOrWhiteSpace($nm)){ $nameMap[[int]$vid] = $nm }
  } catch {}
}

# Include active view explicitly if requested
if($IncludeActiveView){
  try {
    $cv = Get-Result (Invoke-Mcp 'get_current_view' @{} 60 120 -Force)
    $avid = [int]$cv.viewId
    if(-not $nameMap.ContainsKey($avid)){
      $vi = Get-Result (Invoke-Mcp 'get_view_info' @{ viewId = $avid } 60 180 -Force)
      $nm = [string]$vi.view.name; if([string]::IsNullOrWhiteSpace($nm)){ $nm = [string]$vi.name }
      if(-not [string]::IsNullOrWhiteSpace($nm)){ $nameMap[$avid] = $nm }
    }
  } catch {}
}

# Select targets by name pattern
$targetIds = @()
foreach($kv in $nameMap.GetEnumerator()){
  $vid = [int]$kv.Key; $nm = [string]$kv.Value
  if($nm -like $NameLike){ $targetIds += $vid }
}
if($IncludeActiveView -and $cv){ $targetIds += ([int]$cv.viewId) }
$targetIds = @([System.Linq.Enumerable]::ToArray([System.Linq.Enumerable]::Distinct([int[]]$targetIds)))

# Delete clouds in target views across all revisions
$toDelete = @($clouds | Where-Object { try { $targetIds -contains ([int]$_.viewId) } catch { $false } })
$before = $toDelete.Count
foreach($c in $toDelete){
  try{ $eid = [int]$c.elementId } catch { $eid = 0 }
  if($eid -le 0){ continue }
  try { $null = Invoke-Mcp 'delete_revision_cloud' @{ elementId = $eid } 60 240 -Force } catch {}
}

# Re-check
$lr2 = Get-Result (Invoke-Mcp 'list_revisions' @{ includeClouds = $true; cloudFields = @('elementId','viewId') } 120 300 -Force)
$revItems2 = @(); try { $revItems2 = @($lr2.revisions) } catch {}
$clouds2 = @(); foreach($rv in $revItems2){ if($rv.clouds){ $clouds2 += $rv.clouds } }
$remain = @($clouds2 | Where-Object { try { $targetIds -contains ([int]$_.viewId) } catch { $false } }).Count

$out = [pscustomobject]@{ ok=$true; port=$Port; pattern=$NameLike; includeActive=$IncludeActiveView.IsPresent; targetViewCount=$targetIds.Count; deleted=$before; remaining=$remain }
$out | ConvertTo-Json -Depth 6
