Param(
  [string]$SourceDir = 'Projects/AutoCadOut',
  [string]$LayerName = 'A-WALL-____-MCUT',
  [string]$OutDwg = 'Projects/AutoCadOut/merged_by_comment_via_dxf.dwg',
  [string]$AccorePath = 'C:/Program Files/Autodesk/AutoCAD 2026/accoreconsole.exe',
  [string]$Locale = 'ja-JP',
  [int]$TimeoutSec = 600
)
$ErrorActionPreference = 'Stop'
$dir = (Resolve-Path $SourceDir).Path
$dwgs = Get-ChildItem -Path $dir -Filter 'walls_*.dwg' -File | Sort-Object Name
if(-not $dwgs -or $dwgs.Count -lt 2){ throw 'Need at least 2 DWGs in SourceDir' }

# 1) Convert each DWG to DXF using SAVEAS
$dxfs = & (Join-Path $PSScriptRoot 'ConvertDwgsToDxf.ps1') -Dwgs ($dwgs | ForEach-Object { $_.FullName }) -AccorePath $AccorePath -Locale $Locale

# 2) For each DXF, replace exact layer name with suffixed name
foreach($f in $dxfs){
  $stem = ([System.IO.Path]::GetFileNameWithoutExtension($f) -replace '^.*?_','')
  $new = $LayerName + '_' + $stem
  $enc = [System.Text.Encoding]::GetEncoding(932)
  $txt = [System.IO.File]::ReadAllText($f, $enc)
  $txt = $txt -replace "(?m)^8\r?\n" + [regex]::Escape($LayerName) + "\r?$", "8`n" + $new
  $txt = $txt -replace "(?m)^2\r?\n" + [regex]::Escape($LayerName) + "\r?$", "2`n" + $new
  [System.IO.File]::WriteAllText($f, $txt, $enc)
}

# 3) Build DXFIN merge script
$rest = @($dxfs | Sort-Object | Select-Object -Skip 1)
$scr = Join-Path $dir 'merge_via_dxfin.scr'
$lines = @('._CMDECHO 0','._FILEDIA 0','._CMDDIA 0','._ATTDIA 0','._EXPERT 5','._-LAYER _THAW _ALL _ON _ALL _UNLOCK _ALL _')
foreach($f in $rest){ $p = $f.Replace('\\','/'); $lines += ('._-DXFIN "' + $p + '"') }
$lines += '._-PURGE A * N'
$lines += '._-PURGE R * N'
$lines += '._-AUDIT Y'
$outAbs = [System.IO.Path]::GetFullPath($OutDwg)
$lines += ('.__-SAVEAS 2018 "' + ($outAbs.Replace('\\','/')) + '"')
$lines += '._QUIT Y'
$enc = [System.Text.Encoding]::GetEncoding(932)
[System.IO.File]::WriteAllLines($scr, $lines, $enc)

# 4) Run accoreconsole with seed DWG and merge script
$seed = $dwgs[0].FullName
$logBase = [System.IO.Path]::ChangeExtension($outAbs, $null)
$logOut = "$logBase.accore.stdout.log"
$logErr = "$logBase.accore.stderr.log"
try { Remove-Item $logOut,$logErr -Force -ErrorAction SilentlyContinue } catch {}

$args = @('/i', $seed, '/s', $scr, '/l', $Locale)
$p = Start-Process -FilePath $AccorePath -ArgumentList $args -WorkingDirectory ([System.IO.Path]::GetDirectoryName($seed)) -NoNewWindow -RedirectStandardOutput $logOut -RedirectStandardError $logErr -PassThru
$p.WaitForExit($TimeoutSec * 1000) | Out-Null
if(-not $p.HasExited){ try { $p.Kill($true) } catch {}; throw "DXFIN merge timeout (logs: $logOut, $logErr)" }
if(-not (Test-Path $outAbs)){ throw "Merged DWG not produced (logs: $logOut, $logErr)" }
Write-Output (Resolve-Path $outAbs).Path

