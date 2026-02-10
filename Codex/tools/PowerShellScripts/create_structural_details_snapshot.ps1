# @feature: Expanded defaults to improve diff detection coverage | keywords: 柱, スペース, ビュー, スナップショット
param(
  [Parameter(Mandatory=$true)][int]$Port,
  [int]$ChunkSize = 150,
  [int]$WaitSeconds = 600,
  [int]$TimeoutSec = 1800,
  [switch]$DeleteOld,
  [switch]$IncludeTypes = $true,
  [switch]$IncludeTypeParameters = $true,
  # Expanded defaults to improve diff detection coverage
  [string[]]$TypeParamKeys = @('符号','H','B','tw','tf','Type Mark','コメント','構造用途','材質')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8='1'

$SCRIPT_DIR = $PSScriptRoot
$ROOT = (Resolve-Path (Join-Path $SCRIPT_DIR '..\\..')).Path
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'

function Call-Command([string]$Method, [hashtable]$Params){
  $pjson = ($Params | ConvertTo-Json -Depth 60 -Compress)
  $tempFile = [System.IO.Path]::GetTempFileName()
  try {
    & python -X utf8 $PY --port $Port --command $Method --params $pjson --output-file $tempFile --wait-seconds $WaitSeconds --timeout-sec $TimeoutSec | Out-Null
    return (Get-Content -LiteralPath $tempFile -Raw -Encoding UTF8 | ConvertFrom-Json)
  } finally {
    try { Remove-Item -LiteralPath $tempFile -Force -ErrorAction SilentlyContinue } catch {}
  }
}

function Get-ResultPayload($obj){
  if ($null -eq $obj) { return $null }
  if ($obj.result -and $obj.result.result) { return $obj.result.result }
  if ($obj.result) { return $obj.result }
  return $obj
}

function Get-Prop($obj, [string]$name){
  try { if ($null -ne $obj -and $obj.PSObject.Properties.Match($name).Count -gt 0) { return $obj.$name } } catch {}
  return $null
}

# 1) Project info
$projObj = Call-Command 'get_project_info' @{}
$proj = Get-ResultPayload $projObj
if (-not $proj) { throw "Failed to fetch project info from port $Port" }

$projectName = $proj.projectName
$projectNumber = if ($proj.projectNumber) { $proj.projectNumber } else { "X$Port" }

function Sanitize([string]$name){
  if (-not $name) { return "Unknown" }
  $bad = [System.IO.Path]::GetInvalidFileNameChars() + @('\', '/', ':', '*', '?', '"', '<', '>', '|')
  $res = ($name.ToCharArray() | ForEach-Object { if ($bad -contains $_) { '_' } else { $_ } }) -join ''
  return $res.Trim()
}

$projFolder = "{0}_{1}" -f (Sanitize $projectName), (Sanitize $projectNumber)
$logsDir = if($env:MCP_PROJECT_DIR -and (Test-Path -LiteralPath $env:MCP_PROJECT_DIR)) { Join-Path $env:MCP_PROJECT_DIR 'Logs' } else { Join-Path $ROOT (Join-Path 'Work' (Join-Path $projFolder 'Logs')) }
New-Item -ItemType Directory -Force -Path $logsDir | Out-Null

# Delete old details snapshots if requested
if ($DeleteOld) {
  Get-ChildItem -LiteralPath $logsDir -Filter ("structural_details_port{0}_*.json" -f $Port) -File -ErrorAction SilentlyContinue | ForEach-Object {
    try { Remove-Item -LiteralPath $_.FullName -Force } catch {}
  }
}

# 2) Current view
$cvObj = Call-Command 'get_current_view' @{}
$cv = Get-ResultPayload $cvObj
if (-not $cv -or -not $cv.viewId) { throw "Failed to get current view on port $Port" }
$viewId = [int]$cv.viewId

# 3) Visible structural elements in view (ids only)
$catFraming = -2001320
$catColumns = -2001330
$ievParams = @{ viewId = $viewId; categoryIds = @($catFraming, $catColumns); _shape = @{ idsOnly = $true; page = @{ limit = 20000 } }; _filter = @{ modelOnly = $true; excludeImports = $true } }
$ievObj = Call-Command 'get_elements_in_view' $ievParams
$iev = Get-ResultPayload $ievObj

$ids = @()
$ei = Get-Prop $iev 'elementIds'
if ($ei) { $ids = @($ei) }
else {
  $r1 = Get-Prop $iev 'result'
  if ($r1) {
    $ei2 = Get-Prop $r1 'elementIds'
    if ($ei2) { $ids = @($ei2) }
  }
}
$ids = @($ids | ForEach-Object { try { [int]$_ } catch { $null } } | Where-Object { $_ -ne $null })

# 4) Fetch element details in chunks
$all = @()
for($i=0; $i -lt $ids.Count; $i += $ChunkSize){
  $hi = [Math]::Min($i+$ChunkSize-1, $ids.Count-1)
  $chunk = @($ids[$i..$hi])
  $geiObj = Call-Command 'get_element_info' @{ elementIds = $chunk; rich = $true }
  $gei = Get-ResultPayload $geiObj
  if ($gei.elements) { $all += @($gei.elements) }
}

# 5) (Optional) Fetch ElementTypes in view and type parameters
$types = @()
$typeParamsOut = @{}
if ($IncludeTypes) {
  try {
    $gtv = Call-Command 'get_types_in_view' @{ viewId=$viewId; categories=@($catFraming,$catColumns); includeCounts=$true; includeTypeInfo=$true; modelOnly=$true }
    $gtvP = Get-ResultPayload $gtv
    if ($gtvP.types) { $types = @($gtvP.types) }
  } catch {}
}
if ($IncludeTypeParameters -and $types.Count -gt 0) {
  try {
    $typeIds = @($types | ForEach-Object { try { [int]$_.typeId } catch { $null } } | Where-Object { $_ -ne $null } | Sort-Object -Unique)
    if ($typeIds.Count -gt 0) {
      # page in 200s
      $keys = @(); foreach($k in $TypeParamKeys){ if(-not [string]::IsNullOrWhiteSpace($k)){ $keys += @{ name = $k } } }
      $tpAll = @()
      for($i=0; $i -lt $typeIds.Count; $i += 200){
        $slice = @($typeIds[$i..([Math]::Min($i+199,$typeIds.Count-1))])
        $tp = Call-Command 'get_type_parameters_bulk' @{ typeIds = $slice; paramKeys = $keys; page = @{ startIndex = 0; batchSize = 200 } }
        $tpP = Get-ResultPayload $tp
        if ($tpP.items) { $tpAll += @($tpP.items) }
      }
      foreach($it in $tpAll){
        try {
          $tid = [int]$it.typeId
          $typeParamsOut[[string]$tid] = $it
        } catch {}
      }
    }
  } catch {}
}

# 6) Save snapshot file (detailed)
$ts = Get-Date -Format 'yyyyMMdd_HHmmss'
$outfile = Join-Path $logsDir ("structural_details_port{0}_{1}.json" -f $Port, $ts)
$snapshot = [PSCustomObject]@{
  ok = $true
  createdAt = (Get-Date).ToString("o")
  port = $Port
  project = @{ name = $projectName; number = $projectNumber }
  viewId = $viewId
  categoryIds = @($catFraming, $catColumns)
  totalIds = $ids.Count
  count = ($all | Measure-Object).Count
  elements = $all
  types = $types
  typeParameters = $typeParamsOut
}
$snapshot | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $outfile -Encoding UTF8
Write-Host ("Saved: {0}" -f $outfile) -ForegroundColor Green

