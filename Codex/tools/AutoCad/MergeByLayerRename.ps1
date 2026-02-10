Param(
  [string[]]$InputDwgs,
  [Parameter(Mandatory=$true)][string]$LayerName,
  [string[]]$Stems,
  [string]$OutPath = 'Projects/AutoCadOut/merged_by_comment_v2.dwg',
  [string]$AccorePath = 'C:/Program Files/Autodesk/AutoCAD 2026/accoreconsole.exe',
  [string]$Locale = 'ja-JP',
  [int]$TimeoutSec = 180,
  [int]$StableSeconds = 0,
  [int]$PollIntervalSec = 1,
  [switch]$SkipCleanup,
  [int]$PostSuccessSleepSec = 3
)

$ErrorActionPreference = 'Stop'
if(-not $InputDwgs -or $InputDwgs.Count -lt 1){ throw 'InputDwgs required' }
foreach($p in $InputDwgs){ if(-not (Test-Path $p)){ throw "DWG not found: $p" } }
if(-not (Test-Path $AccorePath)){ throw "accoreconsole not found: $AccorePath" }
if(-not $Stems -or $Stems.Count -ne $InputDwgs.Count){
  # Derive stems from file names: walls_<STEM>.dwg
  $Stems = @()
  foreach($p in $InputDwgs){ $bn = [System.IO.Path]::GetFileNameWithoutExtension($p); $s = ($bn -replace '^.*?_',''); $Stems += $s }
}

$seed = (Resolve-Path $InputDwgs[0]).Path
$outs = (Resolve-Path (Split-Path -Parent $OutPath) -ErrorAction SilentlyContinue)
if(-not $outs){ $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutPath) }
$outAbs = (Resolve-Path (New-Item -ItemType File -Force -Path $OutPath)).Path
Remove-Item $outAbs -Force -ErrorAction SilentlyContinue

$scr = [System.IO.Path]::ChangeExtension($outAbs, '.scr')
$lines = New-Object System.Collections.Generic.List[string]

# Prologue settings
$lines.Add('(setvar "CMDECHO" 0)')
$lines.Add('(setvar "FILEDIA" 0)')
$lines.Add('(setvar "CMDDIA" 0)')
$lines.Add('(setvar "ATTDIA" 0)')
$lines.Add('(setvar "EXPERT" 5)')
$lines.Add('(setvar "NOMUTT" 1)')
$lines.Add('(vl-load-com)')

# LISP helpers added via here-strings to avoid quoting issues
$ensureLayer = @"
(defun _ensure-layer (doc name / layers lay)
  (if (and name (> (strlen name) 0))
    (progn
      (setq layers (vla-get-Layers doc))
      (setq lay (vl-catch-all-apply 'vla-Item (list layers name)))
      (if (vl-catch-all-error-p lay) (progn (vla-Add layers name) (setq lay (vla-Item layers name))))
      (vla-put-Lock lay :vlax-false)
      (vla-put-Freeze lay :vlax-false)
      lay)))
"@
$foreachEnt = @"
(defun _foreach-entity-in-all-blocks (doc fn)
  (vlax-for blk (vla-get-Blocks doc) (vlax-for e blk (apply fn (list e)))))
"@
$mergeAX = @"
(defun merge-one-layer-AX (src dst / doc)
  (setq doc (vla-get-ActiveDocument (vlax-get-Acad-Object)))
  (_ensure-layer doc dst)
  (if (tblsearch "LAYER" src)
    (progn
      (_foreach-entity-in-all-blocks doc
        (function (lambda (ent) (if (and (vlax-property-available-p ent 'Layer) (= (strcase (vla-get-Layer ent)) (strcase src))) (vl-catch-all-apply 'vla-put-Layer (list ent dst))))))
      (vl-catch-all-apply (function (lambda () (vla-Delete (vla-Item (vla-get-Layers doc) src)))))))
  (princ))
"@
foreach($ln in ($ensureLayer -split "`r?`n")){ $lines.Add($ln) }
foreach($ln in ($foreachEnt -split "`r?`n")){ $lines.Add($ln) }
foreach($ln in ($mergeAX -split "`r?`n")){ $lines.Add($ln) }

# First doc: rename its own target layer to stemmed
$firstStem = $Stems[0]
$new0 = ("{0}_{1}" -f $LayerName, $firstStem)
$lines.Add('(merge-one-layer-AX "' + $LayerName + '" "' + $new0 + '")')

# Insert others and immediately merge their target layer
for($i=1; $i -lt $InputDwgs.Count; $i++){
  $p = (Resolve-Path $InputDwgs[$i]).Path.Replace('\\','/')
  $stem = $Stems[$i]
  $new = ("{0}_{1}" -f $LayerName, $stem)
  $lines.Add('._-INSERT "' + $p + '" 0,0,0 1 1 0')
  $lines.Add('._EXPLODE')
  $lines.Add('L')
  $lines.Add('(merge-one-layer-AX "' + $LayerName + '" "' + $new + '")')
}

if(-not $SkipCleanup){
  $lines.Add('(command "_.-PURGE" "A" "*" "N")')
  $lines.Add('(command "_.-PURGE" "R" "*" "N")')
  $lines.Add('(command "_AUDIT" "Y")')
}

$outName = [System.IO.Path]::GetFileName($outAbs)
$lines.Add('(command "_.SAVEAS" "2018" "' + $outName + '")')
$lines.Add('(princ)')

# Write script file in Shift-JIS (CP932) to preserve Japanese tokens
$enc = [System.Text.Encoding]::GetEncoding(932)
[System.IO.File]::WriteAllLines($scr, $lines, $enc)

# Real-time logging and monitoring
$outDir = [System.IO.Path]::GetDirectoryName($outAbs)
$logOut = Join-Path $outDir (([System.IO.Path]::GetFileNameWithoutExtension($outAbs))+'.accore.stdout.log')
$logErr = Join-Path $outDir (([System.IO.Path]::GetFileNameWithoutExtension($outAbs))+'.accore.stderr.log')
try { Remove-Item $logOut,$logErr -Force -ErrorAction SilentlyContinue } catch {}

$args = @('/i', $seed, '/s', $scr, '/l', $Locale)
# Use output directory as working directory so SAVEAS with filename writes to expected folder
$procWd = [System.IO.Path]::GetDirectoryName($outAbs)
$p = Start-Process -FilePath $AccorePath -ArgumentList $args -WorkingDirectory $procWd -NoNewWindow -RedirectStandardOutput $logOut -RedirectStandardError $logErr -PassThru

$deadline = (Get-Date).AddSeconds([Math]::Max(5,$TimeoutSec))
$lastSize = -1L
$stableStart = $null
$completedByFile = $false
while(-not $p.HasExited){
  Start-Sleep -Seconds ([Math]::Max(1,$PollIntervalSec))
  if(Test-Path $outAbs){
    if($StableSeconds -le 0){
      $completedByFile = $true; break
    }
    try { $size = (Get-Item $outAbs).Length } catch { $size = -1 }
    if($size -gt 0){
      if($size -eq $lastSize){
        if(-not $stableStart){ $stableStart = Get-Date }
        elseif(((Get-Date) - $stableStart).TotalSeconds -ge [Math]::Max(1,$StableSeconds)){
          $completedByFile = $true; break
        }
      } else { $lastSize = $size; $stableStart = $null }
    }
  }
  if((Get-Date) -gt $deadline){ break }
}

if($completedByFile){
  if($PostSuccessSleepSec -gt 0){ Start-Sleep -Seconds $PostSuccessSleepSec }
  if(-not $p.HasExited){ $null = $p.WaitForExit(10000) }
  if(-not $p.HasExited){ try { $p.Kill($true) } catch {} }
}
else {
  if(-not $p.HasExited){ try { $p.Kill($true) } catch {} }
  Write-Warning "accoreconsole timeout after ${TimeoutSec}s (no stable output)"
  if(Test-Path $logOut){ Write-Host "[stdout tail]" -ForegroundColor DarkGray; Get-Content -Tail 120 -Path $logOut }
  if(Test-Path $logErr){ Write-Host "[stderr tail]" -ForegroundColor DarkGray; Get-Content -Tail 120 -Path $logErr }
  throw "accoreconsole timeout"
}

if(-not (Test-Path $outAbs)){ throw 'Merged DWG not found' }
Write-Output $outAbs

