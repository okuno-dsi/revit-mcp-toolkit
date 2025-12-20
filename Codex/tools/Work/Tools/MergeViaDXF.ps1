Param(
  [string]$SourceDir = 'Work/AutoCadOut',
  [string]$LayerName = 'A-WALL-____-MCUT',
  [string]$OutDwg = 'Work/AutoCadOut/merged_by_comment_via_dxf.dwg',
  [string]$AccorePath = 'C:/Program Files/Autodesk/AutoCAD 2025/accoreconsole.exe',
  [string]$Locale = 'en-US'
)
$ErrorActionPreference = 'Stop'
$dir = (Resolve-Path $SourceDir).Path
$dwgs = Get-ChildItem -Path $dir -Filter 'walls_*.dwg' -File | Sort-Object Name
if(-not $dwgs -or $dwgs.Count -lt 2){ throw 'Need at least 2 DWGs in SourceDir' }

# 1) Convert each DWG to DXF (ASCII) using SAVEAS
$dxfs = & (Join-Path $PSScriptRoot 'ConvertDwgsToDxf.ps1') -Dwgs ($dwgs | ForEach-Object { $_.FullName }) -AccorePath $AccorePath -Locale $Locale

# 2) For each DXF, replace exact layer name with suffixed name
foreach($f in $dxfs){
  $stem = ([System.IO.Path]::GetFileNameWithoutExtension($f) -replace '^.*?_','')
  $new = $LayerName + '_' + $stem
  $txt = Get-Content -Raw -Path $f -Encoding ASCII
  $txt = $txt -replace "(?m)^8\r?\n" + [regex]::Escape($LayerName) + "\r?$", "8`n" + $new
  $txt = $txt -replace "(?m)^2\r?\n" + [regex]::Escape($LayerName) + "\r?$", "2`n" + $new
  Set-Content -Encoding ASCII -Path $f -Value $txt
}

# 3) Build DXFIN merge script
$rest = @($dxfs | Sort-Object | Select-Object -Skip 1)
$scr = Join-Path $dir 'merge_via_dxfin.scr'
$lines = @('._CMDECHO 0','._FILEDIA 0','._CMDDIA 0','._ATTDIA 0','._EXPERT 5','._-LAYER _THAW _ALL _ON _ALL _UNLOCK _ALL _')
foreach($f in $rest){ $p = $f.Replace('\\','/'); $lines += ('._-DXFIN "' + $p + '"') }
$lines += '._-PURGE A * N'
$lines += '._-PURGE R * N'
$lines += '._-AUDIT Y'
$lines += ('.__-SAVEAS 2018 "' + ((Resolve-Path $OutDwg).Path.Replace('\\','/')) + '"')
$lines += '._QUIT Y'
Set-Content -Encoding ASCII -Path $scr -Value $lines

# 4) Run accoreconsole with seed DWG and merge script
$seed = $dwgs[0].FullName
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $AccorePath
$psi.Arguments = "/i `"$seed`" /s `"$scr`" /l $Locale"
$psi.WorkingDirectory = [System.IO.Path]::GetDirectoryName($seed)
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$p = [System.Diagnostics.Process]::Start($psi)
$out = $p.StandardOutput.ReadToEnd(); $err = $p.StandardError.ReadToEnd()
$p.WaitForExit(600000) | Out-Null
if(-not $p.HasExited){ try { $p.Kill($true) } catch {}; throw 'DXFIN merge timeout' }
if(-not (Test-Path $OutDwg)){ Write-Output $out; Write-Output $err; throw 'Merged DWG not produced' }
Write-Output (Resolve-Path $OutDwg).Path
