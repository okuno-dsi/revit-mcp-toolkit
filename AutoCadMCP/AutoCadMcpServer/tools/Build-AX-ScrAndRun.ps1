param(
  [string[]]$Inputs = @(
    "$env:USERPROFILE\\Documents\\Revit_MCP\\Codex\\Work\\AutoCadOut\\walls_A.dwg",
    "$env:USERPROFILE\\Documents\\Revit_MCP\\Codex\\Work\\AutoCadOut\\walls_B.dwg",
    "$env:USERPROFILE\\Documents\\Revit_MCP\\Codex\\Work\\AutoCadOut\\walls_C.dwg",
    "$env:USERPROFILE\\Documents\\Revit_MCP\\Codex\\Work\\AutoCadOut\\walls_D.dwg"
  ),
  [string[]]$Include = @('A-WALL-____-MCUT'),
  [string]$StagingRoot = 'C:/Temp/CadJobs/Staging'
)

New-Item -ItemType Directory -Path $StagingRoot -Force | Out-Null
$jobDir = Join-Path $StagingRoot ("ax_" + (Get-Date -Format yyyyMMdd_HHmmss))
New-Item -ItemType Directory -Path $jobDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $jobDir 'in') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $jobDir 'out') -Force | Out-Null

# Copy inputs
foreach($p in $Inputs){
  if(Test-Path ($p -replace '/','\')){
    Copy-Item -Path ($p -replace '/','\') -Destination (Join-Path $jobDir 'in') -Force
  }
}

$attachLines = @()
foreach($p in (Get-ChildItem -Path (Join-Path $jobDir 'in') -Filter *.dwg -File)){
  $pp = $p.FullName.Replace('\\','/')
  $attachLines += ('(command "_.-XREF" "ATTACH" "{0}" "0,0,0" "1" "1" "0")' -f $pp)
}

$mergeLines = @()
foreach($p in (Get-ChildItem -Path (Join-Path $jobDir 'in') -Filter *.dwg -File)){
  $stem = [System.IO.Path]::GetFileNameWithoutExtension($p.Name)
  foreach($old in $Include){
    $src = "$stem`$0$$old"
    $dst = "${old}_${stem}"
    $mergeLines += ('(merge-one-layer-AX "{0}" "{1}")' -f $src, $dst)
  }
}

$header = @(
  '(setvar "CMDECHO" 0)',
  '(setvar "FILEDIA" 0)',
  '(setvar "CMDDIA" 0)',
  '(setvar "ATTDIA" 0)',
  '(setvar "EXPERT" 5)',
  '(vl-load-com)'
)

$bind = '(command "_.-XREF" "BIND" "*" "")'

$functions = @(
  '(defun _ensure-layer (doc name / layers lay)',
  '  (if (and name (> (strlen name) 0))',
  '    (progn',
  '      (setq layers (vla-get-Layers doc))',
  '      (setq lay (vl-catch-all-apply ''vla-Item (list layers name)))',
  '      (if (vl-catch-all-error-p lay)',
  '        (progn (vla-Add layers name) (setq lay (vla-Item layers name))))',
  '      (vla-put-Lock lay :vlax-false)',
  '      (vla-put-Freeze lay :vlax-false)',
  '      lay)))',
  '',
  '(defun _foreach-entity-in-all-blocks (doc fn)',
  '  (vlax-for blk (vla-get-Blocks doc)',
  '    (vlax-for e blk (apply fn (list e)))))',
  '',
  '(defun merge-one-layer-AX (src dst / doc)',
  '  (setq doc (vla-get-ActiveDocument (vlax-get-Acad-Object)))',
  '  (_ensure-layer doc dst)',
  '  (if (tblsearch "LAYER" src)',
  '    (progn',
  '      (_foreach-entity-in-all-blocks doc',
  '        (function (lambda (ent)',
  '          (if (and (vlax-property-available-p ent ''Layer)',
  '                   (= (strcase (vla-get-Layer ent)) (strcase src)))',
  '              (vl-catch-all-apply ''vla-put-Layer (list ent dst))))))',
  '      (vl-catch-all-apply',
  '        (function (lambda () (vla-Delete (vla-Item (vla-get-Layers doc) src)))))))',
  '  (princ))'
)

$footer = @(
  '(command "_.-PURGE" "A" "*" "N")',
  '(command "_.-PURGE" "R" "*" "N")',
  '(command "_AUDIT" "Y")',
  ('(command "_.SAVEAS" "2018" "{0}")' -f (Join-Path $jobDir 'out\merged.dwg').Replace('\\','/')),
  '(princ)'
)

$scr = @()
$scr += $header
$scr += $attachLines
$scr += $bind
$scr += $functions
$scr += $mergeLines
$scr += $footer

$scrPath = Join-Path $jobDir 'run.scr'
Set-Content -Path $scrPath -Value ($scr -join "`r`n") -Encoding UTF8

Write-Host "JobDir: $jobDir"
Write-Host "Script: $scrPath"

# Launch in separate console
$launcher = Join-Path (Split-Path $PSCommandPath -Parent) 'Run-AccoreLatestJob.ps1'
Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoExit','-ExecutionPolicy','Bypass','-File', $launcher) -WorkingDirectory (Split-Path $PSCommandPath -Parent)
