Param(
  [string]$DwgPath,
  [string]$AccorePath = 'C:/Program Files/Autodesk/AutoCAD 2025/accoreconsole.exe',
  [string]$Locale = 'en-US',
  [string]$OutFile
)
$ErrorActionPreference = 'Stop'
if(-not (Test-Path $DwgPath)){ throw "DWG not found: $DwgPath" }
if(-not (Test-Path $AccorePath)){ throw "accoreconsole not found: $AccorePath" }

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if(-not $OutFile){ $OutFile = [System.IO.Path]::ChangeExtension($DwgPath, '.layers.txt') }
$OutFile = (Resolve-Path (New-Item -ItemType File -Force -Path $OutFile)).Path

$scrPath = [System.IO.Path]::ChangeExtension($OutFile, '.scr')
$outEsc = $OutFile.Replace('\\','/')
$lines = @(
  "(defun dump_layers (outpath / f e name)",
  "  (setq f (open outpath `"w`"))",
  "  (setq e (tblnext `"LAYER`" T))",
  "  (while e",
  "    (setq name (cdr (assoc 2 e)))",
  "    (write-line name f)",
  "    (setq e (tblnext `"LAYER`"))",
  "  )",
  "  (close f)",
  "  (princ)",
  ")",
  "(dump_layers `"$outEsc`")",
  ".__QUIT Y"
)
Set-Content -Encoding ASCII -Path $scrPath -Value $lines

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $AccorePath
$psi.Arguments = "/i `"$DwgPath`" /s `"$scrPath`" /l $Locale"
$psi.WorkingDirectory = [System.IO.Path]::GetDirectoryName($DwgPath)
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$p = [System.Diagnostics.Process]::Start($psi)
$p.WaitForExit(300000) | Out-Null
if(-not $p.HasExited){ try { $p.Kill($true) } catch {}; throw "accoreconsole timeout" }
if($p.ExitCode -ne 0){ Write-Warning ($p.StandardError.ReadToEnd()); Write-Warning ($p.StandardOutput.ReadToEnd()); throw "accoreconsole exit=$($p.ExitCode)" }

Get-Content -Raw -Path $OutFile
