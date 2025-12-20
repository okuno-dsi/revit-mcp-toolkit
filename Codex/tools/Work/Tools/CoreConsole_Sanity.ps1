Param(
  [string]$SeedDwg = 'Work/AutoCadOut/walls_A.dwg',
  [string]$AccorePath = 'C:/Program Files/Autodesk/AutoCAD 2025/accoreconsole.exe',
  [string]$OutDir = 'C:/Temp/CadOut',
  [string]$Locale = 'en-US'
)
$ErrorActionPreference = 'Stop'
if(-not (Test-Path $AccorePath)){ throw "accoreconsole not found: $AccorePath" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$seed = (Resolve-Path $SeedDwg).Path
if(-not (Test-Path $seed)){
  $cand = Get-ChildItem -Path 'Work/AutoCadOut' -Filter 'walls_*.dwg' -File | Select-Object -First 1
  if($cand){ $seed = $cand.FullName } else { throw 'No seed DWG found under Work/AutoCadOut' }
}
$scr = Join-Path $OutDir 'core_sanity.scr'
$outDwg = Join-Path $OutDir 'core_sanity.dwg'
if(Test-Path $outDwg){ Remove-Item $outDwg -Force }
$lines = @(
  '._CMDECHO 0',
  '._FILEDIA 0',
  '._CMDDIA 0',
  '._ATTDIA 0',
  '._EXPERT 5',
  '._-SAVEAS',
  '2018',
  '"' + ($outDwg.Replace('\\','/')) + '"',
  '._QUIT Y'
)
Set-Content -Encoding ASCII -Path $scr -Value $lines

# Run Core Console
Get-Process accoreconsole -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $AccorePath
$psi.Arguments = "/i `"$seed`" /s `"$scr`" /l $Locale"
$psi.WorkingDirectory = (Split-Path -Parent $scr)
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$p = [System.Diagnostics.Process]::Start($psi)
$ok = $p.WaitForExit(180000)
if(-not $ok){ try{ $p.Kill($true) }catch{}; throw 'Core Console timeout' }
$log = $p.StandardOutput.ReadToEnd() + "`n" + $p.StandardError.ReadToEnd()
$logPath = Join-Path $OutDir 'core_sanity.log'
$log | Set-Content -Path $logPath -Encoding UTF8
if(Test-Path $outDwg){ Write-Host ('OK: ' + (Resolve-Path $outDwg).Path) -ForegroundColor Green } else { throw ('NG: output not created. Log: ' + $logPath) }

