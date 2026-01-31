# @feature: export all schedules to csv | keywords: ビュー, 集計表
param(
  [int]$Port = 5210,
  [string]$OutDir,
  [switch]$FillBlanks,
  [switch]$Itemize
)

$ErrorActionPreference = 'Stop'

function Invoke-RevitCommandJson {
  param(
    [Parameter(Mandatory=$true)][string]$Method,
    [hashtable]$Params = @{},
    [int]$Port = 5210
  )
  $paramsJson = if ($Params) { $Params | ConvertTo-Json -Compress } else { '{}' }
  $tmp = New-TemporaryFile
  try {
    $argsList = @(
      "Manuals/Scripts/send_revit_command_durable.py",
      "--port", $Port,
      "--command", $Method,
      "--params", $paramsJson,
      "--output-file", $tmp.FullName
    )
    python @argsList | Out-Null
    $j = Get-Content -Raw -LiteralPath $tmp.FullName | ConvertFrom-Json
    return $j.result.result
  } finally {
    Remove-Item -ErrorAction SilentlyContinue $tmp.FullName
  }
}

function Resolve-LogsDir([int]$p){
  $work = Resolve-Path (Join-Path $PSScriptRoot '..\..\Work')
  $cands = Get-ChildItem -LiteralPath $work -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*_$p" }
  $chosen = $null
  if($cands){ $chosen = ($cands | Where-Object { $_.Name -notlike 'Project_*' } | Select-Object -First 1); if(-not $chosen){ $chosen = $cands | Select-Object -First 1 } }
  if(-not $chosen){ $chosen = New-Item -ItemType Directory -Path (Join-Path $work ("Project_{0}" -f $p)) }
  $logs = Join-Path $chosen.FullName 'Logs'
  if(-not (Test-Path $logs)){ New-Item -ItemType Directory -Path $logs | Out-Null }
  return $logs
}

function Get-ProjectNameSafe {
  param([int]$Port)
  $logs = Resolve-LogsDir -p $Port
  $cands = @(
    (Join-Path $logs ("project_info_{0}.json" -f $Port)),
    (Join-Path $logs 'project_info.json'),
    (Join-Path 'Manuals/Logs' ("project_info_{0}.json" -f $Port)),
    (Join-Path 'Manuals/Logs' 'project_info.json')
  )
  foreach($path in $cands){
    if(Test-Path $path){
      try{
        $j = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
        $name = $j.result.result.projectName
        if($name){ return ($name -replace '[\\/:*?"<>|]', '_') }
      }catch{}
    }
  }
  return ("Project_{0}" -f $Port)
}

function Ensure-Directory { param([string]$Path) if (-not (Test-Path $Path)) { New-Item -ItemType Directory -Path $Path -Force | Out-Null } }
function Sanitize-Name { param([string]$s) return (($s ?? 'Untitled') -replace '[\\/:*?"<>|]', '_').Trim() }

# Resolve output directory
$projName = Get-ProjectNameSafe -Port $Port
if (-not $OutDir) { $OutDir = Join-Path 'Work' ("{0}_{1}\Schedules_CSV" -f $projName, $Port) }
Ensure-Directory -Path $OutDir

Write-Host "[1/2] Fetch schedules..." -ForegroundColor Cyan
$sched = Invoke-RevitCommandJson -Method 'get_schedules' -Params @{} -Port $Port
if (-not $sched -or -not $sched.ok -or -not $sched.schedules) { throw "No schedules returned from Revit." }
$items = @($sched.schedules)
Write-Host ("Found {0} schedules" -f $items.Count)

Write-Host "[2/2] Export CSV per schedule" -ForegroundColor Cyan
foreach ($s in $items) {
  $title = [string]$s.title
  $sid = [int]$s.scheduleViewId
  $safe = Sanitize-Name ("集計表_" + $title)
  $csv = Join-Path $OutDir ($safe + '.csv')
  $abs = [System.IO.Path]::GetFullPath($csv)
  Write-Host ("- Exporting: {0} (id={1}) -> {2}" -f $title, $sid, $abs)
  $params = @{ scheduleViewId = $sid; outputFilePath = $abs; includeHeader = $true }
  if ($FillBlanks) { $params.fillBlanks = $true }
  if ($Itemize) { $params.itemize = $true }
  $res = Invoke-RevitCommandJson -Method 'export_schedule_to_csv' -Params $params -Port $Port
  if (-not $res -or -not $res.ok) { Write-Warning ("Failed: {0}" -f $title) }
}

Write-Host "Done. CSVs at: $OutDir" -ForegroundColor Green
