param(
  [string]$Accore = 'C:\Program Files\Autodesk\AutoCAD 2025\accoreconsole.exe',
  [string]$Locale = 'en-US',
  [string]$Seed = "$env:USERPROFILE\Documents\VS2022\Ver421\Codex\Work\AutoCadOut\seed.dwg",
  [string]$StagingRoot1 = 'C:\CadJobs\Staging',
  [string]$StagingRoot2 = 'C:\Temp\CadJobs\Staging'
)

function Get-LatestScriptPath([string[]]$roots){
  $files = @()
  foreach($r in $roots){ if(Test-Path $r){ $files += Get-ChildItem -Path $r -Filter *.scr -Recurse -ErrorAction SilentlyContinue } }
  if(-not $files){ throw "No .scr files under: $($roots -join ', ')" }
  return $files | Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

try {
  $latestScr = Get-LatestScriptPath @($StagingRoot1,$StagingRoot2)
} catch { Write-Error $_; exit 1 }

$scriptPath = $latestScr.FullName
$jobDir = Split-Path -Parent $scriptPath

if(-not $scriptPath){ Write-Error "No .scr found in $jobDir"; exit 1 }

if(-not (Test-Path $Seed)){
  $firstIn = Get-ChildItem -Path (Join-Path $jobDir 'in') -Filter *.dwg -File -ErrorAction SilentlyContinue | Select-Object -First 1
  if($firstIn){ $Seed = $firstIn.FullName } else { Write-Error "Seed not found and no input DWG in $jobDir\in"; exit 1 }
}

Write-Host "JobDir   : $jobDir"
Write-Host "Script   : $scriptPath"
Write-Host "Seed     : $Seed"
Write-Host "Accore   : $Accore"
Write-Host "Locale   : $Locale"

if(-not (Test-Path $Accore)){ Write-Error "accoreconsole.exe not found: $Accore"; exit 1 }

$wrapper = @"
& "$Accore" /i "$Seed" /s "$scriptPath" /l $Locale
Write-Host ''
Write-Host '==== Press Enter to close ===='
Read-Host | Out-Null
"@

$wrapPath = Join-Path $jobDir "run_accore_console.ps1"
Set-Content -Path $wrapPath -Value $wrapper -Encoding UTF8

# Open in separate PowerShell console and keep it open after run
Start-Process -FilePath "powershell.exe" -ArgumentList @("-NoExit","-ExecutionPolicy","Bypass","-File", $wrapPath) -WorkingDirectory $jobDir -WindowStyle Normal
