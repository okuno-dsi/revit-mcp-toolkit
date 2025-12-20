Param(
  [string]$SourceDir = 'Work/AutoCadOut',
  [string]$OutDir = 'C:/Temp/CadOut',
  [string]$LayerName = 'A-WALL-____-MCUT',
  [string]$AccorePath = 'C:/Program Files/Autodesk/AutoCAD 2025/accoreconsole.exe',
  [string]$Locale = 'en-US',
  [int]$TimeoutSecPerFile = 240,
  [int]$TimeoutSecMerge = 600
)

$ErrorActionPreference = 'Stop'
function Ensure-Path([string]$p){ if(-not (Test-Path $p)){ New-Item -ItemType Directory -Force -Path $p | Out-Null } }

if(-not (Test-Path $AccorePath)){ throw "accoreconsole not found: $AccorePath" }
Ensure-Path $OutDir

$src = (Resolve-Path $SourceDir).Path
$dwgs = Get-ChildItem -Path $src -Filter 'walls_*.dwg' -File | Sort-Object Name
if(-not $dwgs -or $dwgs.Count -lt 2){ throw "Need at least 2 DWGs under $src (found $($dwgs.Count))" }

# Helper: run accore with script
function Run-AccCore([string]$inputDwg,[string]$script,[int]$timeout){
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = $AccorePath
  $psi.Arguments = "/i `"$inputDwg`" /s `"$script`" /l $Locale"
  $psi.WorkingDirectory = (Split-Path -Parent $script)
  $psi.UseShellExecute = $false
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  $p = [System.Diagnostics.Process]::Start($psi)
  if(-not $p.WaitForExit($timeout*1000)){
    try { $p.Kill($true) } catch {}
    throw "accoreconsole timeout for $inputDwg"
  }
  $out = $p.StandardOutput.ReadToEnd() + "`n" + $p.StandardError.ReadToEnd()
  return @{ Exit=$p.ExitCode; Log=$out }
}

# 1) Convert all DWG -> DXF (ASCII) into $OutDir
$dxfs = @()
foreach($f in $dwgs){
  $bn = $f.BaseName
  $dxf = Join-Path $OutDir ($bn + '.dxf')
  if(Test-Path $dxf){ Remove-Item $dxf -Force }
  $scr = Join-Path $OutDir ($bn + '.dxfout.scr')
  $outEsc = $dxf.Replace('\\','/')
  $lines = @(
    '._CMDECHO 0',
    '._FILEDIA 0',
    '._CMDDIA 0',
    '._ATTDIA 0',
    '._EXPERT 5',
    '._-SAVEAS',
    'DXF',
    '2018',
    '"' + $outEsc + '"',
    '._QUIT Y'
  )
  Set-Content -Encoding ASCII -Path $scr -Value $lines
  $run = Run-AccCore -inputDwg $f.FullName -script $scr -timeout $TimeoutSecPerFile
  if(-not (Test-Path $dxf)){
    $log = Join-Path $OutDir ($bn + '.dxfout.log')
    $run.Log | Set-Content -Path $log -Encoding UTF8
    throw "DXF not produced: $dxf (log: $log)"
  }
  $dxfs += (Resolve-Path $dxf).Path
}

# 2) Replace layer names in DXF (code 8 and 2) with suffixed names per file
foreach($dxf in $dxfs){
  $stem = ([IO.Path]::GetFileNameWithoutExtension($dxf) -replace '^.*?_','')
  $new = $LayerName + '_' + $stem
  $txt = [IO.File]::ReadAllText($dxf)
  $pat8 = "(?m)^8\r?\n" + [regex]::Escape($LayerName) + "\r?$"
  $pat2 = "(?m)^2\r?\n" + [regex]::Escape($LayerName) + "\r?$"
  $txt = [regex]::Replace($txt, $pat8, "8`n" + $new)
  $txt = [regex]::Replace($txt, $pat2, "2`n" + $new)
  [IO.File]::WriteAllText($dxf, $txt)
}

# 3) Merge via DXFIN (seed = first DWG)
$rest = @($dxfs | Sort-Object | Select-Object -Skip 1)
$mergeScr = Join-Path $OutDir 'merge_via_dxfin.scr'
  $m = @('._CMDECHO 0','._FILEDIA 0','._CMDDIA 0','._ATTDIA 0','._EXPERT 5')
foreach($p in $rest){ $m += ('._-DXFIN "' + ($p.Replace('\\','/')) + '"') }
$outDwg = (Resolve-Path (Join-Path $src 'merged_by_comment_via_dxf.dwg' ) -ErrorAction SilentlyContinue)
if(-not $outDwg){ $outDwg = Join-Path $src 'merged_by_comment_via_dxf.dwg' } else { $outDwg = $outDwg.Path }
$m += '._-PURGE A * N'
$m += '._-PURGE R * N'
$m += '._-AUDIT Y'
$m += ('.__-SAVEAS 2018 "' + ($outDwg.Replace('\\','/')) + '"')
$m += '._QUIT Y'
Set-Content -Encoding ASCII -Path $mergeScr -Value $m

$seed = $dwgs[0].FullName
$mergeRun = Run-AccCore -inputDwg $seed -script $mergeScr -timeout $TimeoutSecMerge
if(-not (Test-Path $outDwg)){
  $mergeLog = Join-Path $OutDir 'merge_via_dxfin.log'
  $mergeRun.Log | Set-Content -Path $mergeLog -Encoding UTF8
  throw "Merged DWG not produced: $outDwg (log: $mergeLog)"
}

Write-Host "DXF files:" -ForegroundColor Cyan
$dxfs | ForEach-Object { Write-Host " - $_" }
Write-Host "Merged DWG: $outDwg" -ForegroundColor Green
