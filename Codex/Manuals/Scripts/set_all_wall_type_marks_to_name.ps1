param(
  [int]$Port = 5210,
  [int]$JobTimeoutSec = 120,
  [int]$WaitSeconds = 240,
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

Write-Host "[1/3] Fetch all wall types" -ForegroundColor Cyan
$typesRes = Invoke-RevitJson -Method 'get_wall_types' -Params @{ skip=0; count=5000 }
$types = @($typesRes.types)
if(-not $types -or $types.Count -eq 0){ throw 'No wall types returned.' }
$plan = $types | ForEach-Object { [PSCustomObject]@{ typeId = [int]$_.typeId; typeName = [string]$_.typeName } }
$LOGS = Ensure-LogsDir -Port $Port
$planPath = Join-Path $LOGS ("set_all_wall_type_marks_plan_{0}.json" -f $Port)
(@{ ok=$true; count=$plan.Count; items=$plan } | ConvertTo-Json -Depth 6) | Out-File -LiteralPath $planPath -Encoding utf8
Write-Host ("  -> Plan saved: {0}" -f $planPath) -ForegroundColor DarkGreen

if($DryRun){ Write-Host "[DryRun] Skipping updates" -ForegroundColor Yellow; return }

Write-Host "[2/3] Update Type Mark := typeName for all wall types" -ForegroundColor Cyan
$logFile = Join-Path $LOGS ("set_all_wall_type_marks_exec_{0}.jsonl" -f $Port)
if(Test-Path $logFile){ Remove-Item $logFile -Force }
$ok=0; $fail=0
foreach($row in $plan){
  try {
    $res = Invoke-RevitJson -Method 'update_wall_type_parameter' -Params @{ typeId = $row.typeId; builtInName = 'ALL_MODEL_TYPE_MARK'; value = $row.typeName }
    ($res | ConvertTo-Json -Depth 10 -Compress) | Out-File -LiteralPath $logFile -Append -Encoding utf8
    $ok++
    Start-Sleep -Milliseconds 80
  } catch {
    $fail++
    (@{ typeId=$row.typeId; error=$_.Exception.Message } | ConvertTo-Json -Compress) | Out-File -LiteralPath $logFile -Append -Encoding utf8
  }
}
Write-Host ("  -> Summary: Success={0}, Failed={1}. Log={2}" -f $ok, $fail, $logFile) -ForegroundColor Green

Write-Host "[3/3] Done" -ForegroundColor Green

