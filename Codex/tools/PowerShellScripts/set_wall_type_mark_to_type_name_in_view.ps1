# @feature: set wall type mark to type name in view | keywords: 壁, スペース, ビュー
param(
  [int]$Port = 5210,
  [int]$BatchSize = 50,
  [int]$JobTimeoutSec = 180,
  [int]$WaitSeconds = 240,
  [string]$ParamName,
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
function Ensure-LogsDir([int]$Port){
  $workRoot = Join-Path $REPO_ROOT 'Work'
  Ensure-Dir $workRoot
  $cand = Get-ChildItem -LiteralPath $workRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*_$Port" } | Select-Object -First 1
  if(-not $cand){ $cand = New-Item -ItemType Directory -Path (Join-Path $workRoot ("Project_{0}" -f $Port)) }
  $logs = Join-Path $cand.FullName 'Logs'
  Ensure-Dir $logs
  return $logs
}

function Invoke-RevitJson {
  param(
    [string]$Method,
    [hashtable]$Params = @{}
  )
  $paramsJson = if ($Params) { $Params | ConvertTo-Json -Depth 20 -Compress } else { '{}' }
  $tmp = New-TemporaryFile
  try {
    $argsList = @($PY, '--port', $Port, '--command', $Method, '--params', $paramsJson, '--output-file', $tmp.FullName, '--timeout-sec', $JobTimeoutSec, '--wait-seconds', $WaitSeconds)
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

Write-Host "[2/4] Fetch element info (typeId + typeName)" -ForegroundColor Cyan
$infoById = @{}
for($ofs = 0; $ofs -lt $wallIds.Count; $ofs += $BatchSize){
  $slice = $wallIds[$ofs..([math]::Min($ofs+$BatchSize-1, $wallIds.Count-1))]
  $res = Invoke-RevitJson -Method 'get_element_info' -Params @{ elementIds = $slice; rich = $true }
  $els = @(); if($res -and $res.elements){ $els = @($res.elements) }
  foreach($e in $els){ if($e -and $e.elementId){ $infoById[[int]$e.elementId] = $e } }
}

$byType = @{}
foreach($kv in $infoById.GetEnumerator()){
  $e = $kv.Value
  $tid = $null; $tname = $null
  try { $tid = [int]$e.typeId } catch {}
  try { $tname = [string]$e.typeName } catch {}
  if($tid -and -not [string]::IsNullOrWhiteSpace($tname)){
    $byType[$tid] = @{ typeId=$tid; typeName=$tname }
  }
}
if($byType.Count -eq 0){ throw "Failed to collect typeId/typeName from get_element_info." }

$LOGS = Ensure-LogsDir -Port $Port
$plan = $byType.GetEnumerator() | ForEach-Object { [PSCustomObject]@{ typeId=$_.Key; typeName=$_.Value.typeName } }
$planPath = Join-Path $LOGS ("set_wall_type_mark_plan_{0}.json" -f $Port)
(@{ ok=$true; count=$plan.Count; plan=$plan } | ConvertTo-Json -Depth 8) | Out-File -LiteralPath $planPath -Encoding utf8
Write-Host ("  -> Plan saved: {0}" -f $planPath) -ForegroundColor DarkGreen
if($DryRun){ Write-Host "[DryRun] Skipping updates" -ForegroundColor Yellow; return }

Write-Host "[3/4] Update type parameter (Type Mark := typeName)" -ForegroundColor Cyan
$logFile = Join-Path $LOGS ("set_wall_type_mark_exec_{0}.jsonl" -f $Port)
if(Test-Path $logFile){ Remove-Item $logFile -Force }

$ok=0; $fail=0
foreach($row in $plan){
  $tid = [int]$row.typeId
  $tname = [string]$row.typeName
  try {
    # try with explicit paramName per add-in implementation; fallback English name
    $paramCandidates = @()
    if(-not [string]::IsNullOrWhiteSpace($ParamName)){ $paramCandidates += $ParamName }
    $paramCandidates += 'タイプ マーク'
    $paramCandidates += 'Type Mark'

    $did=false
    foreach($pn in $paramCandidates){
      try {
        $res = Invoke-RevitJson -Method 'update_wall_type_parameter' -Params @{ typeId = $tid; paramName = $pn; value = $tname }
        # detect error shape
        if($res -and $res.error){ throw ($res.error.message ?? 'error') }
        if($res -and $res.ok -eq $true){
          ($res | ConvertTo-Json -Depth 10 -Compress) | Out-File -LiteralPath $logFile -Append -Encoding utf8
          $ok++; $did=$true; break
        }
        # if no ok flag, still log and consider success
        ($res | ConvertTo-Json -Depth 10 -Compress) | Out-File -LiteralPath $logFile -Append -Encoding utf8
        $ok++; $did=$true; break
      } catch {
        # try next candidate
        continue
      }
    }
    if(-not $did){ throw "All paramName candidates failed for typeId=$tid" }
    Start-Sleep -Milliseconds 150
  } catch {
    $fail++
    (@{ typeId=$tid; error=$_.Exception.Message } | ConvertTo-Json -Compress) | Out-File -LiteralPath $logFile -Append -Encoding utf8
  }
}
Write-Host ("  -> Summary: Success={0}, Failed={1}. Log={2}" -f $ok, $fail, $logFile) -ForegroundColor Green

Write-Host "[4/4] Done" -ForegroundColor Green
