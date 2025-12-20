Param(
  [Parameter(Mandatory=$true)][string]$DwgPath,
  [string]$AccorePath = 'C:/Program Files/Autodesk/AutoCAD 2025/accoreconsole.exe',
  [string]$Locale = 'en-US'
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
Set-Content -Encoding ASCII -Path $scrPath -Value $lines

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $AccorePath
$psi.Arguments = "/i `"$dwgFull`" /s `"$scrPath`" /l $Locale"
$psi.WorkingDirectory = [System.IO.Path]::GetDirectoryName($dwgFull)
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$p = [System.Diagnostics.Process]::Start($psi)
$out = $p.StandardOutput.ReadToEnd()
$err = $p.StandardError.ReadToEnd()
$p.WaitForExit(300000) | Out-Null
if(-not $p.HasExited){ try { $p.Kill($true) } catch {}; throw "accoreconsole timeout" }
if($p.ExitCode -ne 0){ Write-Output $out; Write-Output $err; throw "accoreconsole exit=$($p.ExitCode)" }

# crude parse: extract tokens that look like layer names from the output lines after '?' listing
$layers = @()
$out -split "`r?`n" | ForEach-Object {
  $line = $_.Trim()
  if($line -match "^(\*?Layer|Name)" ){ }
}
# Fallback: emit full stdout to inspect
Write-Output $out

