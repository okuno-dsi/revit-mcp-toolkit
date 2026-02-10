param(
  [int]$Port = 5210,
  [int]$RadiusMm = 300,
  [int]$RectW = 600,
  [int]$RectH = 400,
  [int]$Wait = 180,
  [int]$Timeout = 600
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8='1'

$PY = Join-Path $PSScriptRoot 'send_revit_command_durable.py'
$outDir = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path ("Projects/RevisionCloud_Debug/{0}" -f (Get-Date -Format 'yyyyMMdd_HHmmss'))
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function Call($method, $params, $outfile){
  $pjson = ($params | ConvertTo-Json -Depth 60 -Compress)
  & python -X utf8 $PY --port $Port --command $method --params $pjson --wait-seconds $Wait --timeout-sec $Timeout --output-file $outfile | Out-Null
}

# 1) Current view and tags
$cur = Join-Path $outDir 'current_view.json'
Call 'get_current_view' @{} $cur
$cv = Get-Content -LiteralPath $cur -Raw -Encoding UTF8 | ConvertFrom-Json
$viewId = [int]$cv.result.result.viewId

$tagsFile = Join-Path $outDir ("tags_in_view_{0}.json" -f $viewId)
Call 'get_tags_in_view' @{ viewId = $viewId } $tagsFile
$tags = (Get-Content -LiteralPath $tagsFile -Raw -Encoding UTF8 | ConvertFrom-Json).result.result.tags
if(-not $tags -or $tags.Count -eq 0){ throw "No tags in view $viewId" }

$roomTag = $tags | Where-Object { $_.categoryName -eq '部屋タグ' } | Select-Object -First 1
if(-not $roomTag){ $roomTag = $tags | Select-Object -First 1 }
$cx = [double]$roomTag.location.x; $cy = [double]$roomTag.location.y

# 2) Ensure a revision id
$revList = Join-Path $outDir 'revisions.json'
Call 'list_revisions' @{} $revList
$revId = 0
try{ $revId = [int](((Get-Content -LiteralPath $revList -Raw -Encoding UTF8 | ConvertFrom-Json).result.result.revisions | Select-Object -Last 1).id) } catch {}
if(-not $revId){ $mk = Join-Path $outDir 'create_default_revision.json'; Call 'create_default_revision' @{} $mk; try { $revId = [int]((Get-Content -LiteralPath $mk -Raw -Encoding UTF8 | ConvertFrom-Json).result.result.revisionId) } catch {} }
if(-not $revId){ throw "Failed to resolve a revisionId" }

# 3) Try circle cloud (if server implements)
$circleOut = Join-Path $outDir 'create_revision_circle.json'
Call 'create_revision_circle' @{ viewId=$viewId; revisionId=$revId; center=@{ x=$cx; y=$cy }; radiusMm=$RadiusMm; segments=24 } $circleOut

# 4) Try rectangle loop cloud
$x0=$cx-($RectW/2); $x1=$cx+($RectW/2); $y0=$cy-($RectH/2); $y1=$cy+($RectH/2)
$loop = @(
  @{ start=@{x=$x0;y=$y0;z=0}; end=@{x=$x1;y=$y0;z=0} },
  @{ start=@{x=$x1;y=$y0;z=0}; end=@{x=$x1;y=$y1;z=0} },
  @{ start=@{x=$x1;y=$y1;z=0}; end=@{x=$x0;y=$y1;z=0} },
  @{ start=@{x=$x0;y=$y1;z=0}; end=@{x=$x0;y=$y0;z=0} }
)
$rectOut = Join-Path $outDir 'create_revision_cloud_rect.json'
Call 'create_revision_cloud' @{ viewId=$viewId; revisionId=$revId; curveLoops=@($loop) } $rectOut

Write-Host "Saved to $outDir" -ForegroundColor Green


