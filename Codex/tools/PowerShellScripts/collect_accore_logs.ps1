# @feature: Resolve default ExportDir to latest AutoCadOut/Export_* if not provided | keywords: スペース, タグ
param(
  [string]$ExportDir,
  [int]$MaxJobs = 3,
  [switch]$IncludeLayerList
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

chcp 65001 > $null
try {
  $utf8NoBom = New-Object System.Text.UTF8Encoding $false
  [Console]::OutputEncoding = $utf8NoBom
  $OutputEncoding = $utf8NoBom
} catch {}

# Resolve default ExportDir to latest AutoCadOut/Export_* if not provided
$ROOT = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$ACWORK = Join-Path $ROOT 'Codex/Projects/AutoCadOut'
if([string]::IsNullOrWhiteSpace($ExportDir)){
  $latest = Get-ChildItem -LiteralPath $ACWORK -Directory -Filter 'Export_*' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if(-not $latest){ throw "No Export_* under $ACWORK. Specify -ExportDir." }
  $ExportDir = $latest.FullName
}
if(!(Test-Path $ExportDir)){ throw "ExportDir not found: $ExportDir" }

$dest = Join-Path $ExportDir ("AccoreLogs_" + (Get-Date -Format 'yyyyMMdd_HHmmss'))
New-Item -ItemType Directory -Path $dest -Force | Out-Null

# Find recent job folders under %TEMP%\CadJobs (MergeBG and optionally LayerList)
$cadRoot = Join-Path $env:TEMP 'CadJobs'
if(!(Test-Path $cadRoot)){ throw "No CadJobs temp root found at: $cadRoot" }

$candidates = @()
if(Test-Path (Join-Path $cadRoot 'MergeBG')){
  $candidates += (Get-ChildItem -LiteralPath (Join-Path $cadRoot 'MergeBG') -Directory | Sort-Object LastWriteTime -Descending)
}
if($IncludeLayerList){
  $candidates += (Get-ChildItem -LiteralPath $cadRoot -Directory -Filter 'LayerList_*' | Sort-Object LastWriteTime -Descending)
}
if(-not $candidates -or $candidates.Count -eq 0){ throw "No job folders found under $cadRoot" }

$toCopy = $candidates | Select-Object -First $MaxJobs
$i=1
$summary = @()
foreach($job in $toCopy){
  $tag = ('job_'+('{0:d2}' -f $i))
  $out = Join-Path $dest $tag
  New-Item -ItemType Directory -Path $out -Force | Out-Null
  $files = @('console_out.txt','console_err.txt','merge_bg.scr','run_perfile.scr','run_perfile_filelayer.scr','run_perfile_entlast.scr','list_layers.scr')
  foreach($f in $files){
    $src = Join-Path $job.FullName $f
    if(Test-Path $src){ Copy-Item -LiteralPath $src -Destination (Join-Path $out $f) -Force }
  }
  $summary += [pscustomobject]@{ Name=$job.Name; Path=$job.FullName; CopiedTo=$out }
  $i++
}

[pscustomobject]@{
  ExportDir = $ExportDir
  Dest      = $dest
  Jobs      = $summary
}



