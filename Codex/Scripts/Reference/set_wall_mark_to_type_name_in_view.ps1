param(
  [int]$Port = 5210,
  [int]$BatchSize = 100,
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
try { chcp 65001 > $null } catch {}
$env:PYTHONUTF8 = '1'
try { [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false) } catch {}
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$SCRIPT_DIR = $PSScriptRoot
$REPO_ROOT = Resolve-Path (Join-Path $SCRIPT_DIR '..\..')
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'

function Ensure-Dir([string]$Path){ if(-not (Test-Path $Path)){ New-Item -ItemType Directory -Path $Path -Force | Out-Null } }
function Sanitize-Name([string]$Name){ return ($Name -replace '[\\/:*?""<>|]', '_').Trim() }

function Ensure-LogsDir([int]$Port){
  $workRoot = Join-Path $REPO_ROOT 'Work'
  Ensure-Dir $workRoot
  # Try existing folder for this port
  $cand = Get-ChildItem -LiteralPath $workRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*_$Port" } | Select-Object -First 1
  if(-not $cand){
    # fallback to Project_<Port>
    $cand = New-Item -ItemType Directory -Path (Join-Path $workRoot ("Project_{0}" -f $Port))
  }
  $logs = Join-Path $cand.FullName 'Logs'
  Ensure-Dir $logs
  return $logs
}

function Invoke-RevitJson {
  param(
    [string]$Method,
    [hashtable]$Params = @{},
    [int]$JobTimeoutSec = 0
  )
  $paramsJson = if ($Params) { $Params | ConvertTo-Json -Depth 20 -Compress } else { '{}' }
  $tmp = New-TemporaryFile
  try {
    $argsList = @($PY, '--port', $Port, '--command', $Method, '--params', $paramsJson, '--output-file', $tmp.FullName)
    if($JobTimeoutSec -gt 0){ $argsList += @('--timeout-sec', $JobTimeoutSec) }
    python @argsList | Out-Null
    $j = Get-Content -Raw -LiteralPath $tmp.FullName -Encoding UTF8 | ConvertFrom-Json
    if($j -and $j.result -and $j.result.result){ return $j.result.result }
    if($j -and $j.result){ return $j.result }
    return $j
  } finally { Remove-Item -ErrorAction SilentlyContinue $tmp.FullName }
}

Write-Host "[1/4] Resolve active view and walls" -ForegroundColor Cyan
$viewInfo = Invoke-RevitJson -Method 'get_current_view' -Params @{}
try { $viewId = [int]$viewInfo.viewId } catch { $viewId = 0 }
if($viewId -le 0){ throw "Could not resolve active view id." }
$shape = @{ idsOnly = $true; page = @{ limit = 200000 } }
$filter = @{ includeClasses = @('Wall') }
$geiv = Invoke-RevitJson -Method 'get_elements_in_view' -Params @{ viewId=$viewId; _shape=$shape; _filter=$filter }
$wallIds = @()
if($geiv -and $geiv.elementIds){ $wallIds = @($geiv.elementIds | ForEach-Object { [int]$_ }) }
if($wallIds.Count -eq 0){ Write-Warning "No walls in the active view."; return }

Write-Host ("  -> Walls found: {0}" -f $wallIds.Count) -ForegroundColor DarkCyan
$LOGS = Ensure-LogsDir -Port $Port

Write-Host "[2/4] Fetch element info (type names)" -ForegroundColor Cyan
# Query in batches if necessary
$infoById = @{}
for($ofs = 0; $ofs -lt $wallIds.Count; $ofs += $BatchSize){
  $slice = $wallIds[$ofs..([math]::Min($ofs+$BatchSize-1, $wallIds.Count-1))]
  $res = Invoke-RevitJson -Method 'get_element_info' -Params @{ elementIds = $slice; rich = $true }
  $els = @()
  if($res -and $res.elements){ $els = @($res.elements) }
  foreach($e in $els){ if($e -and $e.elementId){ $infoById[[int]$e.elementId] = $e } }
}
Write-Host ("  -> Info rows: {0}" -f $infoById.Count) -ForegroundColor DarkCyan

Write-Host "[3/4] Plan Mark := TypeName per wall" -ForegroundColor Cyan
$plan = @()
foreach($wid in $wallIds){
  $e = $infoById[$wid]
  if(-not $e){ continue }
  $typeName = $null
  try { $typeName = [string]$e.typeName } catch {}
  if([string]::IsNullOrWhiteSpace($typeName)){ continue }
  $plan += [PSCustomObject]@{ elementId = $wid; mark = $typeName }
}
$planPath = Join-Path $LOGS ("set_wall_mark_plan_{0}.json" -f $Port)
(@{ ok=$true; count=$plan.Count; viewId=$viewId; plan=$plan } | ConvertTo-Json -Depth 8) | Out-File -LiteralPath $planPath -Encoding utf8
Write-Host ("  -> Plan saved: {0}" -f $planPath) -ForegroundColor DarkGreen
if($DryRun){ Write-Host "[DryRun] Skipping updates" -ForegroundColor Yellow; return }

Write-Host "[4/4] Apply update_wall_parameter (Mark)" -ForegroundColor Cyan
$logFile = Join-Path $LOGS ("set_wall_mark_exec_{0}.jsonl" -f $Port)
if(Test-Path $logFile){ Remove-Item $logFile -Force }
$ok=0; $fail=0
foreach($row in $plan){
  try {
    # update_wall_parameter expects paramName / builtInName / builtInId / guid on the top-level payload
    $res = Invoke-RevitJson -Method 'update_wall_parameter' -Params @{ elementId = $row.elementId; paramName = 'Mark'; value = $row.mark } -JobTimeoutSec 60
    ($res | ConvertTo-Json -Depth 10 -Compress) | Out-File -LiteralPath $logFile -Append -Encoding utf8
    $ok++
  } catch {
    $fail++
    (@{ elementId=$row.elementId; error=$_.Exception.Message } | ConvertTo-Json -Compress) | Out-File -LiteralPath $logFile -Append -Encoding utf8
  }
}
Write-Host ("Done. Success={0}, Failed={1}. Log={2}" -f $ok, $fail, $logFile) -ForegroundColor Green
