Param(
  [Parameter(Mandatory=$true)][string]$DwgPath,
  [string]$AccorePath = 'C:/Program Files/Autodesk/AutoCAD 2025/accoreconsole.exe',
  [string]$Locale = 'en-US',
  [string]$OutDxf
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
Set-Content -Encoding ASCII -Path $scrPath -Value $lines

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $AccorePath
$psi.Arguments = "/i `"$dwgFull`" /s `"$scrPath`" /l $Locale"
$psi.WorkingDirectory = [System.IO.Path]::GetDirectoryName($dwgFull)
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$p = [System.Diagnostics.Process]::Start($psi)
$p.WaitForExit(300000) | Out-Null
if(-not $p.HasExited){ try { $p.Kill($true) } catch {}; throw "accoreconsole timeout" }
if($p.ExitCode -ne 0){ throw "accoreconsole exit=$($p.ExitCode). stdout:`n" + $p.StandardOutput.ReadToEnd() + "`nstderr:`n" + $p.StandardError.ReadToEnd() }

# Parse DXF table LAYER names
if(-not (Test-Path $OutDxf)){ throw "DXF not found: $OutDxf" }
$names = New-Object System.Collections.Generic.List[string]
$insideLayer = $false; $insideTables = $false
Get-Content -Path $OutDxf -Encoding ASCII | ForEach-Object {
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
