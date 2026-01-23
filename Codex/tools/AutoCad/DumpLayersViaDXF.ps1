Param(
  [Parameter(Mandatory=$true)][string]$DwgPath,
  [string]$AccorePath = 'C:/Program Files/Autodesk/AutoCAD 2026/accoreconsole.exe',
  [string]$Locale = 'ja-JP',
  [string]$OutDxf,
  [int]$TimeoutSec = 300
)
$ErrorActionPreference = 'Stop'
if(-not (Test-Path $DwgPath)){ throw "DWG not found: $DwgPath" }
if(-not (Test-Path $AccorePath)){ throw "accoreconsole not found: $AccorePath" }

$dwgFull = (Resolve-Path $DwgPath).Path
if(-not $OutDxf){ $OutDxf = [System.IO.Path]::ChangeExtension($dwgFull, '.dxf') }
if(Test-Path $OutDxf){ Remove-Item $OutDxf -Force }

$scrPath = [System.IO.Path]::ChangeExtension($OutDxf, '.scr')
$dxfEsc = $OutDxf.Replace('\\','/')
$lines = @(
  ".__CMDECHO 0",
  ".__FILEDIA 0",
  ".__CMDDIA 0",
  ".__ATTDIA 0",
  ".__EXPERT 5",
  ".__-DXFOUT",
  "`"$dxfEsc`"",
  "2018",
  "All",
  ".__QUIT Y"
)
$enc = [System.Text.Encoding]::GetEncoding(932)
[System.IO.File]::WriteAllLines($scrPath, $lines, $enc)

$logBase = [System.IO.Path]::ChangeExtension($OutDxf, $null)
$logOut = "$logBase.accore.stdout.log"
$logErr = "$logBase.accore.stderr.log"
try { Remove-Item $logOut,$logErr -Force -ErrorAction SilentlyContinue } catch {}

$args = @('/i', $dwgFull, '/s', $scrPath, '/l', $Locale)
$p = Start-Process -FilePath $AccorePath -ArgumentList $args -WorkingDirectory ([System.IO.Path]::GetDirectoryName($dwgFull)) -NoNewWindow -RedirectStandardOutput $logOut -RedirectStandardError $logErr -PassThru
$p.WaitForExit($TimeoutSec * 1000) | Out-Null
if(-not $p.HasExited){ try { $p.Kill($true) } catch {}; throw "accoreconsole timeout (logs: $logOut, $logErr)" }
if($p.ExitCode -ne 0){ throw "accoreconsole exit=$($p.ExitCode). logs: $logOut, $logErr" }

# Parse DXF table LAYER names
if(-not (Test-Path $OutDxf)){ throw "DXF not found: $OutDxf" }
$names = New-Object System.Collections.Generic.List[string]
$insideLayer = $false; $insideTables = $false
Get-Content -Path $OutDxf -Encoding Default | ForEach-Object {
  $line = $_
  if($line -eq "SECTION"){ $insideTables = $false }
  elseif($line -eq "TABLES"){ $insideTables = $true }
  elseif($insideTables -and $line -eq "TABLE"){ $insideLayer = $false }
  elseif($insideTables -and $line -eq "LAYER"){ $insideLayer = $true }
  elseif($insideLayer -and $line -eq "ENDTAB"){ $insideLayer = $false }
  elseif($insideLayer -and $line -eq "2"){ $script:nextIsName = $true }
  elseif($insideLayer -and $script:nextIsName){ $names.Add($line); $script:nextIsName=$false }
}
$names | Sort-Object -Unique
