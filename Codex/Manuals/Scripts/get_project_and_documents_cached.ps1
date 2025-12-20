param(
  [int]$Port = 5211,
  [string]$Proxy = "http://127.0.0.1:5221",
  [string]$OutDir,
  [int]$TtlSeconds = 0,
  [switch]$Refresh,
  [switch]$Full
)

$ErrorActionPreference = 'Stop'

# Ensure UTF-8 console/output to avoid mojibake when printing Japanese text
try { chcp 65001 > $null } catch {}
$env:PYTHONUTF8 = '1'
try { [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false) } catch {}
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

# Resolve default logs directory under Work/<Project>_<Port>/Logs if not provided
function Resolve-LogsDir([int]$p){
  $workRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\Work')
  $cands = Get-ChildItem -LiteralPath $workRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*_$p" }
  $chosen = $null
  if($cands){ $chosen = ($cands | Where-Object { $_.Name -notlike 'Project_*' } | Select-Object -First 1); if(-not $chosen){ $chosen = $cands | Select-Object -First 1 } }
  if(-not $chosen){ $chosen = New-Item -ItemType Directory -Path (Join-Path $workRoot ("Project_{0}" -f $p)) }
  $logs = Join-Path $chosen.FullName 'Logs'
  if(-not (Test-Path $logs)){ New-Item -ItemType Directory -Path $logs | Out-Null }
  return $logs
}

if (-not $OutDir -or [string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Resolve-LogsDir -p $Port }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

# Ensure cache is present or refreshed
$argsList = @(
  "${PSScriptRoot}\cache_revit_info.py",
  "--proxy", $Proxy,
  "--revit-port", $Port,
  "--out-dir", $OutDir,
  "--ttl-sec", $TtlSeconds,
  "--what", "all"
)
if ($Refresh) { $argsList += "--refresh" }

& python @argsList | Out-Null

$projPath = Join-Path $OutDir ("project_info_{0}.json" -f $Port)
$docsPath = Join-Path $OutDir ("open_documents_{0}.json" -f $Port)

if (-not (Test-Path $projPath)) { throw "Missing cache: $projPath" }
if (-not (Test-Path $docsPath)) { throw "Missing cache: $docsPath" }

$proj = Get-Content -Raw -LiteralPath $projPath -Encoding UTF8 | ConvertFrom-Json
$docs = Get-Content -Raw -LiteralPath $docsPath -Encoding UTF8 | ConvertFrom-Json

if ($Full) {
  # Output full cached JSON
  [PSCustomObject]@{ project = $proj; documents = $docs } | ConvertTo-Json -Depth 20
  return
}

# Output concise summary
$p = $proj.result
$d = $docs.result
$firstDoc = $null
if ($d -and $d.documents) { $firstDoc = $d.documents[0] }

Write-Host "--- Project Info (Port $Port) ---"
Write-Host ("ProjectName: {0}" -f $p.projectName)
Write-Host ("ProjectNumber: {0}" -f $p.projectNumber)
Write-Host ("ClientName: {0}" -f $p.clientName)
Write-Host ("Status: {0}" -f $p.status)
if ($p.site) { Write-Host ("Site: {0}" -f $p.site.placeName) }
if ($p.inputUnits -and $p.internalUnits) { Write-Host ("Units: input={0} / internal={1}" -f $p.inputUnits.Length, $p.internalUnits.Length) }

Write-Host "--- Open Documents ---"
if ($firstDoc) {
  Write-Host ("Title: {0}" -f $firstDoc.title)
  Write-Host ("Path: {0}" -f $firstDoc.path)
  Write-Host ("IsLinked: {0} | Workshared: {1} | Role: {2}" -f $firstDoc.isLinked, $firstDoc.isWorkshared, $firstDoc.role)
  Write-Host ("LinkCount: {0}" -f $firstDoc.linkCount)
} else {
  Write-Host "(no documents)"
}
