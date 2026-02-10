# @feature: restore view workspace | keywords: スペース, ビュー
param(
  [int]$Port = 5210,
  [Nullable[bool]]$IncludeZoom,
  [Nullable[bool]]$Include3dOrientation,
  [Nullable[bool]]$ActivateSavedActiveView,
  [switch]$Wait,
  [int]$PollSeconds = 1,
  [int]$TimeoutSeconds = 180
)

$useEnv = $false
if(-not $PSBoundParameters.ContainsKey('Port') -and $env:REVIT_MCP_PORT){
  try { $Port = [int]$env:REVIT_MCP_PORT; $useEnv = $true } catch {}
}

$SCRIPT_DIR = $PSScriptRoot
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'

function Resolve-LogsDir([int]$p){
  $work = Resolve-Path (Join-Path $SCRIPT_DIR '..\\..\\..\\Projects')
  $cands = Get-ChildItem -LiteralPath $work -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*_$p" }
  $chosen = $null
  if($cands){ $chosen = ($cands | Where-Object { $_.Name -notlike 'Project_*' } | Select-Object -First 1); if(-not $chosen){ $chosen = $cands | Select-Object -First 1 } }
  if(-not $chosen){ $chosen = New-Item -ItemType Directory -Path (Join-Path $work ("Project_{0}" -f $p)) }
  $logs = Join-Path $chosen.FullName 'Logs'
  if(-not (Test-Path $logs)){ New-Item -ItemType Directory -Path $logs | Out-Null }
  return $logs
}
$LOGS = Resolve-LogsDir -p $Port

$params = @{}
if($PSBoundParameters.ContainsKey('IncludeZoom')){ $params.include_zoom = [bool]$IncludeZoom }
if($PSBoundParameters.ContainsKey('Include3dOrientation')){ $params.include_3d_orientation = [bool]$Include3dOrientation }
if($PSBoundParameters.ContainsKey('ActivateSavedActiveView')){ $params.activate_saved_active_view = [bool]$ActivateSavedActiveView }

chcp 65001 > $null
$env:PYTHONUTF8='1'

if($useEnv){ Write-Host "[Port] Using REVIT_MCP_PORT=$Port" -ForegroundColor DarkCyan }

$outPath = Join-Path $LOGS 'restore_view_workspace.json'
Write-Host "[restore_view_workspace] -> $outPath" -ForegroundColor Cyan
if($params.Count -gt 0){
  $json = ($params | ConvertTo-Json -Compress -Depth 6)
  python $PY --port $Port --command restore_view_workspace --params $json --output-file $outPath
} else {
  python $PY --port $Port --command restore_view_workspace --output-file $outPath
}

if(-not $Wait){
  Write-Host "Tip: check progress via get_view_workspace_restore_status.ps1" -ForegroundColor Gray
  exit 0
}

# Wait mode: poll get_view_workspace_restore_status until done/timeout
try {
  $restoreResp = Get-Content -Raw -Encoding UTF8 -Path $outPath | ConvertFrom-Json
} catch {
  Write-Warning "Could not parse restore response from $outPath ($($_.Exception.Message))."
  exit 0
}

$sessionId = $null
try { $sessionId = [string]$restoreResp.result.result.restoreSessionId } catch {}
if(-not $sessionId){
  Write-Warning "restoreSessionId not found in $outPath. Skipping wait loop."
  exit 0
}

Write-Host "[wait] restoreSessionId=$sessionId" -ForegroundColor DarkYellow
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while((Get-Date) -lt $deadline){
  $statusLines = & python $PY --port $Port --command get_view_workspace_restore_status --wait-seconds 60
  $statusText = ($statusLines -join "`n")
  $status = $null
  try { $status = $statusText | ConvertFrom-Json } catch {
    Write-Warning "Invalid JSON from get_view_workspace_restore_status: $($_.Exception.Message)"
    Start-Sleep -Seconds $PollSeconds
    continue
  }

  $st = $null
  try { $st = $status.result.result } catch { $st = $null }
  if(-not $st){
    Write-Warning "Unexpected status payload shape."
    Start-Sleep -Seconds $PollSeconds
    continue
  }

  $active = [bool]$st.active
  $done = [bool]$st.done
  $sid = $null
  try { $sid = [string]$st.sessionId } catch {}
  if($sid -and ($sid -ne $sessionId)){
    Write-Warning "Different sessionId is active: $sid (expected $sessionId)"
  }

  $idx = 0; $total = 0; $phase = ''
  try { $idx = [int]$st.index } catch {}
  try { $total = [int]$st.totalViews } catch {}
  try { $phase = [string]$st.phase } catch {}
  $missing = 0
  try { $missing = [int]$st.missingViews } catch {}

  Write-Host ("[status] active={0} done={1} phase={2} index={3}/{4} missingViews={5}" -f $active,$done,$phase,$idx,$total,$missing) -ForegroundColor Gray

  if($done -or (-not $active)){ break }
  Start-Sleep -Seconds $PollSeconds
}

Write-Host "Done. Last status saved by get_view_workspace_restore_status.ps1 if needed." -ForegroundColor Green



