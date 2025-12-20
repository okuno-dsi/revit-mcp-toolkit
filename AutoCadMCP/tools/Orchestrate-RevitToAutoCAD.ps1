Param(
  [int]$RevitPort = 5210,
  [string]$AutoCadRpcUrl = "http://127.0.0.1:5251/rpc",
  [string]$DWGExportDir = "C:/Users/okuno/Documents/VS2022/Ver351/Codex/DWGExport",
  [string]$ExportRoot = "C:/Exports/DWG_ByComment",
  [string]$AccorePath = "C:/Program Files/Autodesk/AutoCAD 2026/accoreconsole.exe",
  [string]$SeedDwg = "",
  [string]$Locale = "ja-JP",
  [string]$MergedOut = "C:/Exports/Merged/plan_merged.dwg"
)

# Helpers for AutoCAD MCP
function Invoke-JsonRpc {
  Param([string]$Url,[hashtable]$Body)
  $json = ($Body | ConvertTo-Json -Depth 20)
  return Invoke-RestMethod -Uri $Url -Method Post -ContentType 'application/json' -Body $json
}
function Wait-JobResult {
  Param([string]$BaseUrl,[string]$JobId,[int]$IntervalSec=2,[int]$MaxWaitSec=900)
  $elapsed = 0
  while ($elapsed -lt $MaxWaitSec) {
    try {
      $res = Invoke-RestMethod -Uri ("{0}/result/{1}" -f ($BaseUrl -replace '/rpc$',''), $JobId) -Method Get -TimeoutSec 10
      if ($res -and $res.done -eq $true) { return $res }
    } catch {}
    Start-Sleep -Seconds $IntervalSec
    $elapsed += $IntervalSec
  }
  throw "Timeout waiting for jobId=$JobId"
}

# 1) Prepare DWG export via Revit MCP Python runner
if (-not (Test-Path $DWGExportDir)) { throw "DWGExportDir not found: $DWGExportDir" }
New-Item -ItemType Directory -Force -Path $ExportRoot | Out-Null
# Use timestamped subfolder to avoid name collisions
$ts = Get-Date -Format 'yyyyMMdd_HHmmss'
$exportDir = Join-Path $ExportRoot $ts
New-Item -ItemType Directory -Force -Path $exportDir | Out-Null

$cfgPath = Join-Path $DWGExportDir 'DWGExport.txt'
$pyRunner = Join-Path $DWGExportDir 'run_export_dwg_by_param_groups.py'
if (-not (Test-Path $pyRunner)) { throw "Runner not found: $pyRunner" }

# Write minimal config JSON into DWGExport.txt (first JSON object is used by the runner)
$cfg = @{
  jsonrpc = '2.0'; id = 9001; method = 'export_dwg_by_param_groups';
  params = @{
    # viewId は runner が fallback で解決可
    category = 'OST_Walls';
    paramName = 'コメント';
    outputFolder = $exportDir;
    fileNamePrefix = 'A-PLAN_Comments';
    dwgVersion = 'ACAD2018';
    keepTempView = $false;
    rollback = $true;
    asyncMode = $true;
    opTimeoutMs = 900000;
    maxMillisPerPass = 15000;
    maxGroups = 10;
  }
}
$cfg | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 -Path $cfgPath

# Invoke python runner
$env:REVIT_MCP_PORT = $RevitPort
# Ensure Python can import send_revit_command from Codex root
$codexRoot = Split-Path -Parent $DWGExportDir
if ($env:PYTHONPATH) {
  $env:PYTHONPATH = "$codexRoot;$env:PYTHONPATH"
} else {
  $env:PYTHONPATH = $codexRoot
}
# Allow Revit add-in to resolve absolute base URL for post_result
if (-not $env:REVIT_MCP_BASE) {
  $env:REVIT_MCP_BASE = "http://127.0.0.1:$RevitPort"
}
$python = $null
foreach ($cand in @('py','python3','python')) { if (Get-Command $cand -ErrorAction SilentlyContinue) { $python = $cand; break } }
if (-not $python) { throw 'Python interpreter not found. Install Python or ensure it is in PATH.' }

Write-Host "Running Revit DWG export via $python ..."
Push-Location $DWGExportDir
try {
  $summaryJson = & $python $pyRunner 2>&1 | Out-String
} finally { Pop-Location }

Write-Host "Revit export summary:"; Write-Host $summaryJson

# Parse runner summary and collect output DWG paths per group
$summary = $null
try { $summary = $summaryJson | ConvertFrom-Json -ErrorAction Stop } catch {}
$outputPaths = @()
if ($summary -and $summary.passes) {
  foreach ($p in $summary.passes) {
    if ($p.outputs) {
      foreach ($o in $p.outputs) {
        if ($o -is [string]) { if (Test-Path $o) { $outputPaths += (Resolve-Path $o).Path } }
        elseif ($o.path) { if (Test-Path $o.path) { $outputPaths += (Resolve-Path $o.path).Path } }
      }
    }
  }
}
# Fallback: scan directory
if (-not $outputPaths -or $outputPaths.Count -lt 1) {
  # Wait for DWGs to appear (async add-in may still be running)
  $deadline = (Get-Date).AddMinutes(10)
  do {
    $outputPaths = (Get-ChildItem -Path $exportDir -Filter *.dwg -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -ExpandProperty FullName)
    if ($outputPaths -and $outputPaths.Count -gt 0) { break }
    Start-Sleep -Seconds 3
  } while ((Get-Date) -lt $deadline)
}
if (-not $outputPaths -or $outputPaths.Count -lt 1) { throw "No DWG files found in $exportDir" }
Write-Host ("Found {0} DWGs for merge" -f $outputPaths.Count)

# 2) Merge via AutoCAD MCP
Write-Host "Submitting merge job to $AutoCadRpcUrl ..."
$mergeReq = @{ jsonrpc='2.0'; id=2; method='merge_dwgs'; params = @{
  inputs=@($outputPaths);
  output=$MergedOut;
  # レイヤ名は不定のため、ここではマップせずにクリーンアップのみ
  layerStrategy=@{ mode='map'; map=@{ }; deleteEmptyLayers=$true };
  postProcess=@{ purge=$true; audit=$true };
  accore=@{ path=$AccorePath; locale=$Locale; seed=$SeedDwg; timeoutMs=600000 };
  stagingPolicy=@{ root='C:/CadJobs/Staging'; atomicWrite=$true; keepTempOnError=$true }
}}

$mergeResp = Invoke-JsonRpc -Url $AutoCadRpcUrl -Body $mergeReq
if (-not $mergeResp.result -or -not $mergeResp.result.jobId) { throw "Merge submission failed: $($mergeResp | ConvertTo-Json -Depth 5)" }
$mergeJobId = $mergeResp.result.jobId
Write-Host "Merge job accepted: $mergeJobId. Waiting for completion..."

$mergeRes = Wait-JobResult -BaseUrl $AutoCadRpcUrl -JobId $mergeJobId -MaxWaitSec 1800
Write-Host "Merge job completed."
($mergeRes | ConvertTo-Json -Depth 10)
