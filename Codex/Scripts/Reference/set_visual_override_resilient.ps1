param(
  [int]$Port = 5210,
  [int]$ElementId,
  [int]$R = 255,
  [int]$G = 0,
  [int]$B = 0,
  [int]$Transparency = 60,
  [int]$ServerTimeoutSec = 300,
  [int]$WaitSeconds = 240,
  [int]$ViewId,
  [string]$ViewName = "MCP_Working_3D",
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
$LOGS = Resolve-Path (Join-Path $SCRIPT_DIR '..\Logs')

function Out-JsonLog([string]$name,[string]$content){
  try{
    $p = Join-Path $LOGS $name
    $content | Out-File -FilePath $p -Encoding UTF8
    Write-Host "[log] $p" -ForegroundColor DarkGray
  } catch {}
}

function Write-Utf8NoBom([string]$path,[string]$text){
  $enc = New-Object System.Text.UTF8Encoding($false)
  [System.IO.File]::WriteAllText($path, $text, $enc)
}

if($ElementId -le 0){ Write-Error "Invalid or missing -ElementId."; exit 2 }

# 1) Ensure a safe view to apply overrides
if(-not $PSBoundParameters.ContainsKey('ViewId') -or $ViewId -le 0){
  $vname = "${ViewName}_$([DateTime]::Now.ToString('HHmmss'))"
  $createObj = @{ name = $vname; __smoke_ok = $true }
  $createJson = ($createObj | ConvertTo-Json -Compress -Depth 4)
  Write-Host "[create_3d_view] name=$vname" -ForegroundColor Cyan
  if(-not $DryRun){
    $createPath = Join-Path $LOGS 'set_visual_override_resilient.create_3d_view.payload.json'
    Write-Utf8NoBom $createPath $createJson
    $outCreate = & python $PY --port $Port --command create_3d_view --params-file $createPath --timeout-sec $ServerTimeoutSec --wait-seconds $WaitSeconds
    Out-JsonLog "set_visual_override_resilient.create_3d_view.json" $outCreate
    try{ $obj = $outCreate | ConvertFrom-Json } catch { Write-Error "create_3d_view returned invalid JSON"; exit 1 }
    $ok = $obj.result.result.ok
    if(-not $ok){ Write-Error "create_3d_view failed."; exit 1 }
    $ViewId = [int]$obj.result.result.viewId
  } else {
    Write-Host "[DryRun] Would create 3D view: $vname" -ForegroundColor DarkYellow
  }
}
if($ViewId -le 0){ Write-Error "Failed to resolve a valid ViewId."; exit 2 }

# 2) Smoke test payload
$smokeObj = @{ method = 'set_visual_override'; params = @{ elementId = $ElementId; color = @{ r=$R; g=$G; b=$B }; transparency = $Transparency; viewId = $ViewId } }
$smokeJson = ($smokeObj | ConvertTo-Json -Compress -Depth 6)
if($useEnv){ Write-Host "[Port] Using REVIT_MCP_PORT=$Port" -ForegroundColor DarkCyan }
Write-Host "[smoke_test] viewId=$ViewId elementId=$ElementId" -ForegroundColor Cyan
if($DryRun){
  Out-JsonLog "set_visual_override_resilient.smoke.dryrun.json" $smokeJson
  Write-Host "[DryRun] Skipping network calls." -ForegroundColor DarkYellow
  exit 0
}
$smokePath = Join-Path $LOGS 'set_visual_override_resilient.smoke.payload.json'
Write-Utf8NoBom $smokePath $smokeJson
$smokeOut = & python $PY --port $Port --command smoke_test --params-file $smokePath --timeout-sec ([Math]::Min($ServerTimeoutSec,120)) --wait-seconds 60
Out-JsonLog "set_visual_override_resilient.smoke.json" $smokeOut
try{ $sm = $smokeOut | ConvertFrom-Json } catch { Write-Error "Smoke test returned invalid JSON"; exit 1 }
if(-not ($sm.result.result.ok)){ Write-Error "Smoke test failed."; exit 1 }

# 3) Execute override with longer server timeout and wait
$execObj = @{ elementId = $ElementId; color = @{ r=$R; g=$G; b=$B }; transparency=$Transparency; __smoke_ok=$true; viewId = $ViewId }
$execJson = ($execObj | ConvertTo-Json -Compress -Depth 6)
Write-Host "[execute] set_visual_override viewId=$ViewId elementId=$ElementId" -ForegroundColor Yellow
$execPath = Join-Path $LOGS 'set_visual_override_resilient.exec.payload.json'
Write-Utf8NoBom $execPath $execJson
$execOut = & python $PY --port $Port --command set_visual_override --params-file $execPath --timeout-sec $ServerTimeoutSec --wait-seconds $WaitSeconds
Out-JsonLog "set_visual_override_resilient.exec.json" $execOut
try{ $ex = $execOut | ConvertFrom-Json } catch { Write-Error "Execution returned invalid JSON"; exit 1 }
if($ex.error){
  Write-Error ($execOut)
  exit 1
}
Write-Host "Done. Applied override on viewId=$ViewId" -ForegroundColor Green
