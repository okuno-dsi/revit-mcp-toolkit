Param(
  [string]$SeedDwg = 'Projects/AutoCadOut/walls_A.dwg',
  [string]$AccorePath = 'C:/Program Files/Autodesk/AutoCAD 2026/accoreconsole.exe',
  [string]$OutDir = 'C:/Temp/CadOut',
  [string]$Locale = 'ja-JP',
  [int]$TimeoutSec = 180
)
$ErrorActionPreference = 'Stop'
if(-not (Test-Path $AccorePath)){ throw "accoreconsole not found: $AccorePath" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$seed = (Resolve-Path $SeedDwg).Path
if(-not (Test-Path $seed)){
  $cand = Get-ChildItem -Path 'Projects/AutoCadOut' -Filter 'walls_*.dwg' -File | Select-Object -First 1
  if($cand){ $seed = $cand.FullName } else { throw 'No seed DWG found under Projects/AutoCadOut' }
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
$enc = [System.Text.Encoding]::GetEncoding(932)
[System.IO.File]::WriteAllLines($scr, $lines, $enc)

# Run Core Console
Get-Process accoreconsole -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
$logBase = Join-Path $OutDir 'core_sanity'
$logOut = "$logBase.accore.stdout.log"
$logErr = "$logBase.accore.stderr.log"
try { Remove-Item $logOut,$logErr -Force -ErrorAction SilentlyContinue } catch {}

$args = @('/i', $seed, '/s', $scr, '/l', $Locale)
$p = Start-Process -FilePath $AccorePath -ArgumentList $args -WorkingDirectory (Split-Path -Parent $scr) -NoNewWindow -RedirectStandardOutput $logOut -RedirectStandardError $logErr -PassThru
$ok = $p.WaitForExit($TimeoutSec * 1000)
if(-not $ok){ try{ $p.Kill($true) }catch{}; throw "Core Console timeout (logs: $logOut, $logErr)" }
if($p.ExitCode -ne 0){ throw "accoreconsole exit=$($p.ExitCode). logs: $logOut, $logErr" }
if(Test-Path $outDwg){ Write-Host ('OK: ' + (Resolve-Path $outDwg).Path) -ForegroundColor Green } else { throw ("NG: output not created. logs: $logOut, $logErr") }

