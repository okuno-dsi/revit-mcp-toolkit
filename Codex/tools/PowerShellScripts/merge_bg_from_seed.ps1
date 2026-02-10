# @feature: merge bg from seed | keywords: スペース, DWG
param(
  [string]$ExportDir,
  [string]$OutputPath,
  [string]$AccorePath = 'C:/Program Files/Autodesk/AutoCAD 2025/accoreconsole.exe',
  [string]$Locale     = 'en-US',
  [int]$TimeoutMs     = 600000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

chcp 65001 > $null
try {
  $utf8NoBom = New-Object System.Text.UTF8Encoding $false
  [Console]::OutputEncoding = $utf8NoBom
  $OutputEncoding = $utf8NoBom
} catch {}

$ROOT = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$WORK = Join-Path $ROOT 'Codex/Projects/AutoCadOut'

if([string]::IsNullOrWhiteSpace($ExportDir)){
  $latest = Get-ChildItem -LiteralPath $WORK -Directory -Filter 'Export_*' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if(-not $latest){ throw "No Export_* folder under $WORK. Specify -ExportDir." }
  $ExportDir = $latest.FullName
}
if(-not (Test-Path $ExportDir)){
  throw "ExportDir not found: $ExportDir"
}

if([string]::IsNullOrWhiteSpace($OutputPath)){
  $OutputPath = Join-Path $ExportDir 'Merged_B_G.dwg'
}

$SeedPath = Join-Path $ExportDir 'seed.dwg'
$BPath    = Join-Path $ExportDir 'B.dwg'
$GPath    = Join-Path $ExportDir 'G.dwg'

foreach($p in @($AccorePath,$SeedPath,$BPath,$GPath)){
  if(-not (Test-Path $p)){
    throw "Required file missing: $p"
  }
}

# Prepare ASCII-safe copies for Core Console file paths
[Text.Encoding]::RegisterProvider([Text.CodePagesEncodingProvider]::Instance) | Out-Null
$enc=[Text.Encoding]::GetEncoding(932) # Shift-JIS for .scr

$root = Join-Path $env:TEMP 'CadJobs/MergeBG'
New-Item -ItemType Directory -Path $root -Force | Out-Null
$runDir = Join-Path $root (Get-Date -Format 'yyyyMMdd_HHmmssfff')
New-Item -ItemType Directory -Path $runDir -Force | Out-Null

function To-AsciiName([string]$name){
  $base=[IO.Path]::GetFileNameWithoutExtension($name)
  $ext=[IO.Path]::GetExtension($name)
  $san = ($base -replace '[^A-Za-z0-9_-]','_')
  if([string]::IsNullOrWhiteSpace($san)){ $san='dwg' }
  return $san + $ext
}

$inputs = @(
  [pscustomobject]@{ Src=$BPath; Stem='B' },
  [pscustomobject]@{ Src=$GPath; Stem='G' }
)

$asciiPaths=@(); $stems=@();
foreach($it in $inputs){
  $san = To-AsciiName (Split-Path $it.Src -Leaf)
  $dest = Join-Path $runDir $san
  Copy-Item -LiteralPath $it.Src -Destination $dest -Force
  $asciiPaths += $dest
  $stems += $it.Stem
}

# Build script: INSERT/EXPLODE each, then move newly-created entities to constant layer (B or G)
$scrPath = Join-Path $runDir 'merge_bg.scr'
$lines = @()
$lines += '._CMDECHO 0'
$lines += '._FILEDIA 0'
$lines += '._CMDDIA 0'
$lines += '._ATTDIA 0'
$lines += '._EXPERT 5'

$lines += @'
; Move entities created since oldlast to constant layer (exclude 0/DEFPOINTS)
(defun relayer_move_new_to_const (const oldlast / newlast ent e lname)
  (setq newlast (entlast))
  (if (not (tblsearch "LAYER" const)) (command "_.-LAYER" "_N" const ""))
  (setq ent (entnext oldlast))
  (while ent
    (setq e (entget ent))
    (setq lname (cdr (assoc 8 e)))
    (if (and lname (not (= lname "0")) (not (= lname "DEFPOINTS")))
      (progn
        (if (assoc 8 e)
          (progn (setq e (subst (cons 8 const) (assoc 8 e) e)) (entmod e))
        )
      )
    )
    (if (= ent newlast) (setq ent nil) (setq ent (entnext ent)))
  )
  (princ)
)
'@

for($k=0; $k -lt $asciiPaths.Count; $k++){
  $p = $asciiPaths[$k]
  $stem = $stems[$k]
  $norm = ($p -replace '\\','/')
  $lines += '(setq __oldlast (entlast))'
  $lines += (".__-INSERT `"$norm`" 0,0,0 1 1 0")
  $lines += '._EXPLODE L'
  $lines += ("(relayer_move_new_to_const `"$stem`" __oldlast)")
}

$lines += '._-PURGE A * N'
$lines += '._-PURGE R * N'
$lines += '._-PURGE A * N'

$normOut = ($OutputPath -replace '\\','/')
$outDir = Split-Path -Parent $OutputPath; if(-not (Test-Path $outDir)){ New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$lines += ("._SAVEAS 2018 `"$normOut`"")
$lines += '._QUIT Y'
$lines += '(princ)'

[IO.File]::WriteAllText($scrPath, (($lines -join "`r`n")+"`r`n"), $enc)

# Run accoreconsole
$outLog = Join-Path $runDir 'console_out.txt'
$errLog = Join-Path $runDir 'console_err.txt'
$psiArgs = @('/i',$SeedPath,'/s',$scrPath,'/l',$Locale)
$proc = Start-Process -FilePath $AccorePath -ArgumentList $psiArgs -NoNewWindow -PassThru -RedirectStandardOutput $outLog -RedirectStandardError $errLog
$finished = $proc.WaitForExit($TimeoutMs)
if(-not $finished){ try{ Stop-Process -Id $proc.Id -Force } catch{} }
$exit = if($finished){ $proc.ExitCode } else { -1 }
$exists = Test-Path $OutputPath

[pscustomobject]@{
  Mode='MergeBG_CoreConsole'
  ExportDir=$ExportDir
  Output=$OutputPath
  OutputExists=$exists
  ExitCode=$exit
  Script=$scrPath
  RunDir=$runDir
  OutLog=$outLog
  ErrLog=$errLog
}


