Param(
  [Parameter(Mandatory=$true)][string[]]$Dwgs,
  [Parameter(Mandatory=$true)][string]$OutDir,
  [string]$AccorePath = 'C:/Program Files/Autodesk/AutoCAD 2026/accoreconsole.exe',
  [string]$Locale = 'ja-JP',
  [int]$TimeoutSec = 240
)
$ErrorActionPreference = 'Stop'
if(-not (Test-Path $AccorePath)){ throw "accoreconsole not found: $AccorePath" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$outRoot = (Resolve-Path $OutDir).Path

function To-Dxf([string]$dwg){
  $dwgFull = (Resolve-Path $dwg).Path
  $bn = [System.IO.Path]::GetFileNameWithoutExtension($dwgFull)
  $outDxf = Join-Path $outRoot ($bn + '.dxf')
  if(Test-Path $outDxf){ Remove-Item $outDxf -Force }
  $scr = Join-Path $outRoot ($bn + '.dxfout.scr')
  $outEsc = $outDxf.Replace('\\','/')
  $lines = @(
    '._CMDECHO 0',
    '._FILEDIA 0',
    '._CMDDIA 0',
    '._ATTDIA 0',
    '._EXPERT 5',
    '._-DXFOUT',
    '"' + $outEsc + '"',
    '2018',
    'All',
    '._QUIT Y'
  )
  $enc = [System.Text.Encoding]::GetEncoding(932)
  [System.IO.File]::WriteAllLines($scr, $lines, $enc)
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = $AccorePath
  $psi.Arguments = "/i `"$dwgFull`" /s `"$scr`" /l $Locale"
  $psi.WorkingDirectory = $outRoot
  $psi.UseShellExecute = $false
  $logBase = [System.IO.Path]::ChangeExtension($outDxf, '.accore')
  $logOut = $logBase + '.stdout.log'
  $logErr = $logBase + '.stderr.log'
  try { Remove-Item $logOut,$logErr -Force -ErrorAction SilentlyContinue } catch {}
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  $p = [System.Diagnostics.Process]::Start($psi)
  $outAll = $p.StandardOutput.ReadToEnd()
  $errAll = $p.StandardError.ReadToEnd()
  $outAll | Set-Content -Path $logOut -Encoding UTF8
  $errAll | Set-Content -Path $logErr -Encoding UTF8
  $ok = $p.WaitForExit($TimeoutSec*1000)
  if(-not $ok){ try{ $p.Kill($true) }catch{}; throw "DXFOUT timeout: $dwgFull (log: $logOut)" }
  if(-not (Test-Path $outDxf)){ throw "DXF not produced: $outDxf (log: $logOut)" }
  return (Resolve-Path $outDxf).Path
}

$outList = @()
foreach($d in $Dwgs){ $outList += (To-Dxf $d) }
$outList | ForEach-Object { Write-Output $_ }
