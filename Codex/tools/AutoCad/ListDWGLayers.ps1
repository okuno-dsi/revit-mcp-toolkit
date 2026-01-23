Param(
  [string]$DwgPath,
  [string]$AccorePath = 'C:/Program Files/Autodesk/AutoCAD 2026/accoreconsole.exe',
  [string]$Locale = 'ja-JP',
  [string]$OutFile,
  [int]$TimeoutSec = 300
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
$enc = [System.Text.Encoding]::GetEncoding(932)
[System.IO.File]::WriteAllLines($scrPath, $lines, $enc)

$logBase = [System.IO.Path]::ChangeExtension($OutFile, $null)
$logOut = "$logBase.accore.stdout.log"
$logErr = "$logBase.accore.stderr.log"
try { Remove-Item $logOut,$logErr -Force -ErrorAction SilentlyContinue } catch {}

$args = @('/i', $DwgPath, '/s', $scrPath, '/l', $Locale)
$p = Start-Process -FilePath $AccorePath -ArgumentList $args -WorkingDirectory ([System.IO.Path]::GetDirectoryName($DwgPath)) -NoNewWindow -RedirectStandardOutput $logOut -RedirectStandardError $logErr -PassThru
$p.WaitForExit($TimeoutSec * 1000) | Out-Null
if(-not $p.HasExited){ try { $p.Kill($true) } catch {}; throw "accoreconsole timeout (logs: $logOut, $logErr)" }
if($p.ExitCode -ne 0){ throw "accoreconsole exit=$($p.ExitCode). logs: $logOut, $logErr" }

Get-Content -Raw -Path $OutFile
