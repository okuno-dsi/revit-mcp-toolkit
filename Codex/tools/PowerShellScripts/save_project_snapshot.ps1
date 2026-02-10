# @feature: Resolve paths | keywords: ビュー, スナップショット
param(
  [int]$Port = 5210,
  [string]$OutRoot = "Projects/Snapshots",
  [int]$MaxViews = 5,
  [int]$WaitSeconds = 600,
  [int]$TimeoutSec = 1200,
  [switch]$IncludeElements,
  [int]$MaxElementsPerView = 500,
  [int]$ChunkSize = 200
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8='1'

# Resolve paths
$SCRIPT_DIR = $PSScriptRoot
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'

# Snapshot folder
$ts = Get-Date -Format 'yyyyMMdd_HHmmss'
$snapDir = Join-Path (Resolve-Path (Join-Path $SCRIPT_DIR '..\..')).Path $OutRoot
$snapDir = Join-Path $snapDir $ts
New-Item -ItemType Directory -Force -Path $snapDir | Out-Null

function Call($method, $params, $outfile){
  $pjson = ($params | ConvertTo-Json -Depth 60 -Compress)
  & python -X utf8 $PY --port $Port --command $method --params $pjson --output-file $outfile --wait-seconds $WaitSeconds --timeout-sec $TimeoutSec | Out-Null
}

# 1) Project info + open documents/views/current view
$projPath = Join-Path $snapDir 'project_info.json'
Call 'get_project_info' @{} $projPath

$docsPath = Join-Path $snapDir 'open_documents.json'
Call 'get_open_documents' @{} $docsPath

$viewsPath = Join-Path $snapDir 'open_views.json'
Call 'list_open_views' @{} $viewsPath

$curViewPath = Join-Path $snapDir 'current_view.json'
Call 'get_current_view' @{} $curViewPath

# 2) Element ids for first N open views (lightweight idsOnly)
try {
  $views = (Get-Content -LiteralPath $viewsPath -Raw -Encoding UTF8 | ConvertFrom-Json).result.result.views
} catch { $views = @() }

if ($views -and $views.Count -gt 0) {
  $take = [Math]::Min([int]$MaxViews, [int]$views.Count)
  for($i=0; $i -lt $take; $i++){
    try {
      $vid = [int]$views[$i].viewId
      $out = Join-Path $snapDir ("view_{0}_ids.json" -f $vid)
      $params = @{ viewId = $vid; _shape = @{ idsOnly = $true } }
      Call 'get_elements_in_view' $params $out

      if ($IncludeElements) {
        # parse elementIds from saved file
        try {
          $data = Get-Content -LiteralPath $out -Raw -Encoding UTF8 | ConvertFrom-Json
          # try typical envelopes: result.result.elementIds or result.elementIds or elementIds
          $ids = $null
          if ($data.result -and $data.result.result -and $data.result.result.elementIds) { $ids = @($data.result.result.elementIds) }
          elseif ($data.result -and $data.result.elementIds) { $ids = @($data.result.elementIds) }
          elseif ($data.elementIds) { $ids = @($data.elementIds) }
          else { $ids = @() }
        } catch { $ids = @() }

        if ($ids.Count -gt 0) {
          $ids = @($ids | ForEach-Object { $_ })
          if ($ids.Count -gt $MaxElementsPerView) { $ids = $ids[0..($MaxElementsPerView-1)] }
          $elemOut = Join-Path $snapDir ("view_{0}_elements.json" -f $vid)
          $all = @()
          for($k=0; $k -lt $ids.Count; $k += $ChunkSize){
            $hi = [Math]::Min($k+$ChunkSize-1, $ids.Count-1)
            $chunk = @($ids[$k..$hi])
            $temp = Join-Path $snapDir ("view_{0}_elements_part_{1}.json" -f $vid, $k)
            Call 'get_element_info' @{ elementIds = $chunk; rich = $true } $temp
            try {
              $part = Get-Content -LiteralPath $temp -Raw -Encoding UTF8 | ConvertFrom-Json
              if ($part.result -and $part.result.result -and $part.result.result.elements) { $all += @($part.result.result.elements) }
              elseif ($part.result -and $part.result.elements) { $all += @($part.result.elements) }
              elseif ($part.elements) { $all += @($part.elements) }
            } catch {}
            try { Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue } catch {}
          }
          $snapshot = [PSCustomObject]@{ ok = $true; viewId = $vid; count = ($all | Measure-Object).Count; elements = $all }
          $snapshot | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $elemOut -Encoding UTF8
        }
      }
    } catch { }
  }
}

Write-Host ("Saved snapshot: {0}" -f $snapDir) -ForegroundColor Green


