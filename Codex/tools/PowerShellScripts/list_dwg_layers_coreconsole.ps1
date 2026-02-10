# @feature: list dwg layers coreconsole | keywords: DWG
param(
  [Parameter(Mandatory=$true)][string]$DwgPath,
  [string]$AccorePath = 'C:/Program Files/Autodesk/AutoCAD 2025/accoreconsole.exe',
  [string]$Locale     = 'en-US',
  [int]$TimeoutMs     = 300000,
  [switch]$WriteNextToDwg
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

chcp 65001 > $null
try {
  $utf8NoBom = New-Object System.Text.UTF8Encoding $false
  [Console]::OutputEncoding = $utf8NoBom
  $OutputEncoding = $utf8NoBom
} catch {}

if(!(Test-Path $DwgPath)){ throw "DWG not found: $DwgPath" }
if(!(Test-Path $AccorePath)){ throw "accoreconsole.exe not found: $AccorePath" }

[Text.Encoding]::RegisterProvider([Text.CodePagesEncodingProvider]::Instance) | Out-Null
$enc=[Text.Encoding]::GetEncoding(932)

$runDir = Join-Path $env:TEMP ("CadJobs/LayerList_"+(Get-Date -Format 'yyyyMMdd_HHmmssfff'))
New-Item -ItemType Directory -Path $runDir -Force | Out-Null
$outTxt = if($WriteNextToDwg){
  Join-Path (Split-Path -Parent $DwgPath) (([IO.Path]::GetFileNameWithoutExtension($DwgPath))+'.layers.txt')
} else {
  Join-Path $runDir 'layers.txt'
}
$normOut = ($outTxt -replace '\\','/')
$scr = Join-Path $runDir 'list_layers.scr'

$lines = @()
$lines += '._CMDECHO 0'
$lines += '._FILEDIA 0'
$lines += '._CMDDIA 0'
$lines += '._EXPERT 5'
$lines += @'
(defun dump_layers_to_file (path / f e)
  (setq f (open path "w"))
  (if f (progn
    (setq e (tblnext "LAYER" T))
    (while e
      (write-line (cdr (assoc 2 e)) f)
      (setq e (tblnext "LAYER"))
    )
    (close f)
  ))
  (princ)
)
'@
$lines += ("(dump_layers_to_file `"$normOut`")")
$lines += '._QUIT Y'
$lines += '(princ)'
[IO.File]::WriteAllText($scr, (($lines -join "`r`n")+"`r`n"), $enc)

$outLog = Join-Path $runDir 'console_out.txt'
$errLog = Join-Path $runDir 'console_err.txt'
$psi = @('/i',$DwgPath,'/s',$scr,'/l',$Locale)
$proc = Start-Process -FilePath $AccorePath -ArgumentList $psi -NoNewWindow -PassThru -RedirectStandardOutput $outLog -RedirectStandardError $errLog
$null = $proc.WaitForExit($TimeoutMs)
if($proc.ExitCode -ne 0){ throw "accoreconsole exit code: $($proc.ExitCode)" }
if(!(Test-Path $outTxt)){ throw "Layer dump not found: $outTxt" }
$layers = Get-Content -LiteralPath $outTxt -Encoding UTF8 | Where-Object { $_ -and ($_ -ne '') } | Sort-Object -Unique

[pscustomobject]@{
  DwgPath = (Resolve-Path $DwgPath).Path
  OutFile = $outTxt
  Count   = $layers.Count
  Layers  = $layers
}


