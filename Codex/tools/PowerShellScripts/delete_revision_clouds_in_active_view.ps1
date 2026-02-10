# @feature: delete revision clouds in active view | keywords: スペース, ビュー
param(
  [Parameter(Mandatory=$true)][int]$Port,
  [int]$WaitSec = 120,
  [int]$JobTimeoutSec = 300,
  [switch]$AllInLatestRevision,
  [switch]$AllCloudsAllRevisions
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

# Scope selection
$scope = ''
if($AllCloudsAllRevisions){ $scope = 'all-clouds' }
elseif($AllInLatestRevision){ $scope = 'latest-revision' } else { $scope = 'active-view' }

if(-not $AllInLatestRevision -and -not $AllCloudsAllRevisions){
  # Active view id
  $cv = Get-Result (Invoke-Mcp 'get_current_view' @{} 60 120 -Force)
  $viewId = [int]$cv.viewId
} else {
  $viewId = 0
}

# List revisions with clouds
$lr = Get-Result (Invoke-Mcp 'list_revisions' @{ includeClouds = $true; cloudFields = @('elementId','viewId') } 60 240 -Force)

function Get-AllClouds($revList){
  $arr = @(); try { foreach($rv in $revList){ if($rv.clouds){ $arr += $rv.clouds } } } catch {}
  return $arr
}

$revItems = @(); try { $revItems = @($lr.revisions) } catch {}
if($revItems.Count -eq 0){
  $out = [pscustomobject]@{ ok=$true; port=$Port; viewId=($viewId); scope=$scope; deleted=0; remaining=0 }
  $out | ConvertTo-Json -Depth 5
  exit 0
}

if($AllCloudsAllRevisions){
  $targets = @(Get-AllClouds $revItems)
  $before = $targets.Count
  $deletedTotal = 0
  foreach($c in $targets){
    try{ $eid = [int]$c.elementId } catch { $eid = 0 }
    if($eid -le 0){ continue }
    try { $null = Invoke-Mcp 'delete_revision_cloud' @{ elementId = $eid } 60 240 -Force; $deletedTotal++ } catch {}
  }
  # refresh
  $lr = Get-Result (Invoke-Mcp 'list_revisions' @{ includeClouds = $true; cloudFields = @('elementId','viewId') } 60 240 -Force)
  $revItems = @(); try { $revItems = @($lr.revisions) } catch {}
}
else {
  $deletedTotal = 0
  for($pass=0; $pass -lt 3; $pass++){
    $targets = @()
    if($AllInLatestRevision){
      $latest = $revItems[-1]
      $revId = 0; try { $revId = [int]$latest.revisionId } catch {}
      $targets = @(Get-AllClouds @($latest))
    } else {
      $targets = @(Get-AllClouds $revItems | Where-Object { try { [int]$_.viewId -eq $viewId } catch { $false } })
    }
    if($pass -eq 0){ $before = $targets.Count }
    if($targets.Count -eq 0){ break }
    foreach($c in $targets){
      try{ $eid = [int]$c.elementId } catch { $eid = 0 }
      if($eid -le 0){ continue }
      try { $null = Invoke-Mcp 'delete_revision_cloud' @{ elementId = $eid } 60 240 -Force; $deletedTotal++ } catch {}
    }
    # refresh rev list after each pass
    $lr = Get-Result (Invoke-Mcp 'list_revisions' @{ includeClouds = $true; cloudFields = @('elementId','viewId') } 60 240 -Force)
    $revItems = @(); try { $revItems = @($lr.revisions) } catch {}
  }
}

# Remaining after passes
if($AllCloudsAllRevisions){
  $remain = @(Get-AllClouds $revItems).Count
}
elseif($AllInLatestRevision){
  $latest2 = $revItems[-1]
  $remainArr = @(); try { if($latest2.clouds){ $remainArr = $latest2.clouds } } catch {}
  $remain = $remainArr.Count
} else {
  $remain = @(Get-AllClouds $revItems | Where-Object { try { [int]$_.viewId -eq $viewId } catch { $false } }).Count
}

$out = [pscustomobject]@{ ok=$true; port=$Port; viewId=($viewId); scope=$scope; deleted=$deletedTotal; remaining=$remain }
$out | ConvertTo-Json -Depth 5

