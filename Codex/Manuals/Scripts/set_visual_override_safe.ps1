param(
  [int]$Port = 5210,
  [int]$ElementId,
  [int]$R = 255,
  [int]$G = 0,
  [int]$B = 0,
  [int]$Transparency = 60,
  [switch]$Force,
  [switch]$DryRun
)

chcp 65001 > $null
$env:PYTHONUTF8='1'
$useEnv = $false
if(-not $PSBoundParameters.ContainsKey('Port') -and $env:REVIT_MCP_PORT){
  try { $Port = [int]$env:REVIT_MCP_PORT; $useEnv = $true } catch {}
}
$SCRIPT_DIR = $PSScriptRoot
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'
function Resolve-LogsDir([int]$p){
  $work = Resolve-Path (Join-Path $SCRIPT_DIR '..\..\Work')
  $cands = Get-ChildItem -LiteralPath $work -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*_$p" }
  $chosen = $null
  if($cands){ $chosen = ($cands | Where-Object { $_.Name -notlike 'Project_*' } | Select-Object -First 1); if(-not $chosen){ $chosen = $cands | Select-Object -First 1 } }
  if(-not $chosen){ $chosen = New-Item -ItemType Directory -Path (Join-Path $work ("Project_{0}" -f $p)) }
  $logs = Join-Path $chosen.FullName 'Logs'
  if(-not (Test-Path $logs)){ New-Item -ItemType Directory -Path $logs | Out-Null }
  return $logs
}
$LOGS = Resolve-LogsDir -p $Port

function Get-JsonOk([object]$obj){
  if($null -ne $obj.ok){ return [bool]$obj.ok }
  if($obj.result -and $obj.result.result -and $null -ne $obj.result.result.ok){ return [bool]$obj.result.result.ok }
  return $false
}
function Get-JsonSeverity([object]$obj){
  if($obj.result -and $obj.result.result -and $null -ne $obj.result.result.severity){ return [string]$obj.result.result.severity }
  return ''
}

# Resolve ElementId if not provided
if(-not $ElementId){
  $evPath = Join-Path $LOGS 'elements_in_view.json'
  if(!(Test-Path $evPath)){
    # fallback to Manuals/Logs for compatibility
    $evPath = Join-Path (Resolve-Path (Join-Path $SCRIPT_DIR '..\Logs')) 'elements_in_view.json'
    if(!(Test-Path $evPath)){
      Write-Error "elements_in_view.json not found. Run list_elements_in_view.ps1 first or pass -ElementId explicitly."; exit 2
    }
  }
  $data = Get-Content $evPath -Raw | ConvertFrom-Json
  $rows = $data.result.result.rows
  if(-not $rows){ Write-Error "No rows found in elements_in_view.json (expected at result.result.rows)."; exit 2 }
  $ElementId = [int](
    ($rows | Where-Object { $_.categoryId -eq -2000011 -and $_.elementId -gt 0 } | Select-Object -First 1).elementId
  )
  if($ElementId -le 0){
    $ElementId = [int](($rows | Where-Object { $_.elementId -gt 0 } | Select-Object -First 1).elementId)
  }
}
if($ElementId -le 0){ Write-Error "Invalid elementId=$ElementId. Provide a valid element ID with -ElementId or refresh elements_in_view.json."; exit 2 }

# Build smoke_test payload
$smokeObj = @{ method = 'set_visual_override'; params = @{ elementId = $ElementId; color = @{ r=$R; g=$G; b=$B }; transparency = $Transparency } }
$smokeJson = ($smokeObj | ConvertTo-Json -Compress -Depth 6)
if($useEnv){ Write-Host "[Port] Using REVIT_MCP_PORT=$Port" -ForegroundColor DarkCyan }
Write-Host "[smoke_test] set_visual_override elementId=$ElementId" -ForegroundColor Cyan
$drySmokePath = Join-Path $LOGS 'set_visual_override.smoke.dryrun.json'
if($DryRun){ $smokeJson | Out-File -FilePath $drySmokePath -Encoding UTF8; Write-Host "[DryRun] Wrote $drySmokePath" -ForegroundColor DarkYellow }
if($DryRun){ Write-Host "[DryRun] Skipping network calls." -ForegroundColor DarkYellow; exit 0 }
$smokeOut = & python $PY --port $Port --command smoke_test --params $smokeJson --wait-seconds 60
$smokePath = Join-Path $LOGS 'set_visual_override.smoke.json'
if($smokeOut){ $smokeOut | Out-File -FilePath $smokePath -Encoding UTF8 }

try{ $smoke = $smokeOut | ConvertFrom-Json } catch { Write-Error "Smoke test returned invalid JSON: $smokeOut"; exit 1 }
$ok = Get-JsonOk $smoke
$sev = Get-JsonSeverity $smoke
if(-not $ok){ Write-Error "Smoke test failed. See $smokePath"; exit 1 }
if(($sev -eq 'warn') -and (-not $Force)){
  Write-Warning "Smoke test severity=warn. Re-run with -Force to proceed."
  exit 3
}

# Execute real command with __smoke_ok:true
$execObj = @{ elementId = $ElementId; color = @{ r=$R; g=$G; b=$B }; transparency=$Transparency; __smoke_ok=$true }
$execJson = ($execObj | ConvertTo-Json -Compress -Depth 6)
Write-Host "[execute] set_visual_override elementId=$ElementId" -ForegroundColor Yellow
$execOut = & python $PY --port $Port --command set_visual_override --params $execJson --wait-seconds 120
$execPath = Join-Path $LOGS 'set_visual_override.exec.json'
if($execOut){ $execOut | Out-File -FilePath $execPath -Encoding UTF8 }
try{ $exec = $execOut | ConvertFrom-Json } catch { Write-Error "Execution returned invalid JSON: $execOut"; exit 1 }

Write-Host "Done. Results saved:" -ForegroundColor Green
Write-Host " - Smoke: $smokePath"
Write-Host " - Exec : $execPath"

