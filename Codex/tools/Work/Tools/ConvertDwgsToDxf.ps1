Param(
  [Parameter(Mandatory=$true)][string[]]$Dwgs,
  [string]$AccorePath = 'C:/Program Files/Autodesk/AutoCAD 2025/accoreconsole.exe',
  [string]$Locale = 'en-US',
  [int]$TimeoutSec = 180
)
$ErrorActionPreference = 'Stop'
if(-not $Dwgs -or $Dwgs.Count -lt 1){ throw 'Dwgs required' }
if(-not (Test-Path $AccorePath)){ throw "accoreconsole not found: $AccorePath" }

function Convert-One($dwg){
  $dwgFull = (Resolve-Path $dwg).Path
  $outDxf = [System.IO.Path]::ChangeExtension($dwgFull, '.dxf')
  if(Test-Path $outDxf){ Remove-Item $outDxf -Force }
  $scrPath = [System.IO.Path]::ChangeExtension($dwgFull, '.dxfout.scr')
  $outEsc = $outDxf.Replace('\\','/')
  $lines = @(
    '._CMDECHO 0',
    '._FILEDIA 0',
    '._CMDDIA 0',
    '._ATTDIA 0',
    '._EXPERT 5',
    '._-SAVEAS',
    'DXF',
    '2018',
    ('"' + $outEsc + '"'),
    '._QUIT Y'
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
  $outAll = $p.StandardOutput.ReadToEnd() + "`n" + $p.StandardError.ReadToEnd()
  if(-not $p.WaitForExit($TimeoutSec*1000)){ try { $p.Kill($true) } catch {}; throw "DXF convert timeout: $dwgFull`n$outAll" }
  if(-not (Test-Path $outDxf)){ throw "DXF not produced: $outDxf`n$outAll" }
  return $outDxf
}

$dxfs = @()
foreach($d in $Dwgs){ $dxfs += (Convert-One $d) }
$dxfs
