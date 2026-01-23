Param(
  [Parameter(Mandatory=$true)][string]$DwgPath,
  [string]$AccorePath = 'C:/Program Files/Autodesk/AutoCAD 2026/accoreconsole.exe',
  [string]$Locale = 'ja-JP',
  [int]$TimeoutSec = 300
)
$ErrorActionPreference = 'Stop'
if(-not (Test-Path $DwgPath)){ throw "DWG not found: $DwgPath" }
if(-not (Test-Path $AccorePath)){ throw "accoreconsole not found: $AccorePath" }

$dwgFull = (Resolve-Path $DwgPath).Path
$scrPath = [System.IO.Path]::ChangeExtension($dwgFull, '.layers.scr')
$lines = @(
  ".__CMDECHO 0",
  ".__FILEDIA 0",
  ".__CMDDIA 0",
  ".__ATTDIA 0",
  ".__EXPERT 5",
  ".__-LAYER",
  "?",
  "*",
  ".__QUIT Y"
)
$enc = [System.Text.Encoding]::GetEncoding(932)
[System.IO.File]::WriteAllLines($scrPath, $lines, $enc)

$logBase = [System.IO.Path]::ChangeExtension($dwgFull, $null)
$logOut = "$logBase.accore.stdout.log"
$logErr = "$logBase.accore.stderr.log"
try { Remove-Item $logOut,$logErr -Force -ErrorAction SilentlyContinue } catch {}

$args = @('/i', $dwgFull, '/s', $scrPath, '/l', $Locale)
$p = Start-Process -FilePath $AccorePath -ArgumentList $args -WorkingDirectory ([System.IO.Path]::GetDirectoryName($dwgFull)) -NoNewWindow -RedirectStandardOutput $logOut -RedirectStandardError $logErr -PassThru
$p.WaitForExit($TimeoutSec * 1000) | Out-Null
if(-not $p.HasExited){ try { $p.Kill($true) } catch {}; throw "accoreconsole timeout (logs: $logOut, $logErr)" }
if($p.ExitCode -ne 0){ throw "accoreconsole exit=$($p.ExitCode). logs: $logOut, $logErr" }
$out = Get-Content -Raw -Path $logOut -ErrorAction SilentlyContinue

# crude parse: extract tokens that look like layer names from the output lines after '?' listing
$layers = @()
$out -split "`r?`n" | ForEach-Object {
  $line = $_.Trim()
  if($line -match "^(\*?Layer|Name)" ){ }
}
# Fallback: emit full stdout to inspect
Write-Output $out
