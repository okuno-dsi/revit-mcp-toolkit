# @feature: export struct views to dwg | keywords: 柱, 梁, スペース, ビュー, タグ, DWG
param(
  [int]$Port = 5210,
  [string]$OutDir = "Projects/AutoCadOut",
  [int]$WaitSeconds = 300,
  [int]$TimeoutSec = 900,
  [string]$BaseViewName = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null | Out-Null
$env:PYTHONUTF8 = '1'

$scriptDir = $PSScriptRoot
$root = (Resolve-Path (Join-Path $scriptDir "..\..")).Path
$py = Join-Path $scriptDir 'send_revit_command_durable.py'

function Invoke-Mcp {
  param(
    [string]$Method,
    [hashtable]$Params,
    [int]$Wait = $WaitSeconds,
    [int]$JobTimeout = $TimeoutSec
  )
  $payloadJson = $Params | ConvertTo-Json -Depth 50 -Compress
  $tmp = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ("mcp_" + [System.IO.Path]::GetRandomFileName() + ".json"))
  $args = @('--port', $Port, '--command', $Method, '--params', $payloadJson, '--wait-seconds', [string]$Wait, '--output-file', $tmp)
  if ($JobTimeout -gt 0) {
    $args += @('--timeout-sec', [string]$JobTimeout)
  }
  & python -X utf8 $py @args | Out-Null
  $exit = $LASTEXITCODE
  if ($exit -ne 0) {
    $txt = ''
    try { $txt = Get-Content -LiteralPath $tmp -Raw -Encoding UTF8 } catch {}
    if (Test-Path $tmp) { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
    throw "MCP call failed ($Method): $txt"
  }
  $text = ''
  try { $text = Get-Content -LiteralPath $tmp -Raw -Encoding UTF8 } catch {}
  if (Test-Path $tmp) { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
  if ([string]::IsNullOrWhiteSpace($text)) {
    throw "Empty response from MCP ($Method)"
  }
  return $text | ConvertFrom-Json -Depth 200
}

function Get-ResultPayload([object]$obj) {
  if ($null -eq $obj) { return $null }
  if ($obj.result -and $obj.result.result) { return $obj.result.result }
  if ($obj.result) { return $obj.result }
  return $obj
}

function Get-ActiveViewInfo {
  if (-not [string]::IsNullOrWhiteSpace($BaseViewName)) {
    # 明示的にベースビュー名が指定された場合、そのビューを検索
    $viewsResp = Invoke-Mcp -Method 'get_views' -Params @{ includeTemplates = $false; detail = $true }
    $views = @(Get-ResultPayload $viewsResp).views
    $target = $views | Where-Object { $_.name -eq $BaseViewName } | Select-Object -First 1
    if (-not $target) {
      throw "Base view '$BaseViewName' was not found. Please open the correct view or adjust -BaseViewName."
    }
    return @{ viewId = [int]$target.viewId; name = [string]$target.name }
  }
  else {
    # 従来通りアクティブビューを使用
    $resp = Invoke-Mcp -Method 'get_current_view' -Params @{}
    $payload = Get-ResultPayload $resp
    $viewId = 0
    $viewName = ''
    try { $viewId = [int]$payload.viewId } catch {}
    try { $viewName = [string]$payload.name } catch {}
    if ($viewId -le 0) {
      throw "Could not resolve active viewId from get_current_view."
    }
    if ([string]::IsNullOrWhiteSpace($viewName)) {
      try {
        $info = Invoke-Mcp -Method 'get_view_info' -Params @{ viewId = $viewId }
        $ip = Get-ResultPayload $info
        try { $viewName = [string]$ip.view.name } catch {}
        if ([string]::IsNullOrWhiteSpace($viewName)) { $viewName = "View_$viewId" }
      } catch {
        $viewName = "View_$viewId"
      }
    }
    return @{ viewId = $viewId; name = $viewName }
  }
}

function Assert-BaseViewHasStructContent {
  param(
    [int]$ViewId,
    [string]$ViewName
  )
  # 構造柱(-2001330) や 通芯(-2000220) が 1つも無いビューからは実行しない
  $catsResp = Invoke-Mcp -Method 'get_categories_used_in_view' -Params @{ viewId = $ViewId }
  $catsPayload = Get-ResultPayload $catsResp
  $cats = @()
  if ($catsPayload.categories) { $cats = @($catsPayload.categories) }

  $hasFrames = $false
  $hasColumns = $false
  $hasGrids = $false
  foreach ($c in $cats) {
    try {
      $cid = [int]$c.categoryId
      switch ($cid) {
        -2001320 { $hasFrames = $true }   # 構造フレーム
        -2001330 { $hasColumns = $true }  # 構造柱
        -2000220 { $hasGrids = $true }    # 通芯
      }
    } catch {}
  }

  if (-not $hasColumns -or -not $hasGrids) {
    $msg = "Base view '$ViewName' (id=$ViewId) does not contain required categories: "
    if (-not $hasColumns) { $msg += "[StructuralColumns(-2001330)] " }
    if (-not $hasGrids) { $msg += "[Grids(-2000220)] " }
    $msg += "Please run from a full structural plan view (e.g. RSL1) or specify -BaseViewName."
    throw $msg
  }
}

function Duplicate-View {
  param(
    [int]$ViewId,
    [string]$Suffix
  )
  $dupParams = @{ viewId = $ViewId; withDetailing = $true; __smoke_ok = $true }
  $dupResp = Invoke-Mcp -Method 'duplicate_view' -Params $dupParams
  $dupPayload = Get-ResultPayload $dupResp
  $newId = 0
  try { $newId = [int]$dupPayload.viewId } catch {}
  if ($newId -le 0) { throw "duplicate_view did not return viewId." }

  if (-not [string]::IsNullOrWhiteSpace($Suffix)) {
    $newName = "$($Suffix)_$newId"
    try {
      [void](Invoke-Mcp -Method 'rename_view' -Params @{ viewId = $newId; newName = $newName })
    } catch {
      # keep server-assigned name on failure
    }
  }

  return $newId
}

function Isolate-StructFraming {
  param([int]$ViewId)
  # OST_StructuralFraming = -2001320
  # OST_StructuralFramingTags = -2005015
  $params = @{
    viewId             = $ViewId
    detachViewTemplate = $true
    reset              = $true
    keepAnnotations    = $false
    filter             = @{
      includeCategoryIds = @(-2001320, -2005015)
      modelOnly          = $false
    }
  }
  [void](Invoke-Mcp -Method 'isolate_by_filter_in_view' -Params $params)
}

function Isolate-StructColumns {
  param([int]$ViewId)
  # OST_StructuralColumns = -2001330
  # OST_StructuralColumnsTags = -2005018
  $params = @{
    viewId             = $ViewId
    detachViewTemplate = $true
    reset              = $true
    keepAnnotations    = $false
    filter             = @{
      includeCategoryIds = @(-2001330, -2005018)
      modelOnly          = $false
    }
  }
  [void](Invoke-Mcp -Method 'isolate_by_filter_in_view' -Params $params)
}

function Isolate-Grids {
  param([int]$ViewId)
  $params = @{
    viewId             = $ViewId
    detachViewTemplate = $true
    reset              = $true
    keepAnnotations    = $false
    filter             = @{
      # OST_Grids = -2000220 ("通芯")
      includeCategoryIds = @(-2000220)
      modelOnly          = $false
    }
  }
  [void](Invoke-Mcp -Method 'isolate_by_filter_in_view' -Params $params)
}

function Export-ViewToDwg {
  param(
    [int]$ViewId,
    [string]$FileName
  )
  $outAbs = (Resolve-Path (Join-Path $root $OutDir)).Path
  $outAbsNorm = $outAbs.Replace('\','/')
  $exportParams = @{
    viewId       = $ViewId
    outputFolder = $outAbsNorm
    fileName     = $FileName
    dwgVersion   = 'ACAD2018'
    __smoke_ok   = $true
  }
  [void](Invoke-Mcp -Method 'export_dwg' -Params $exportParams -Wait $WaitSeconds -JobTimeout ($TimeoutSec * 2))
}

# Main
$active = Get-ActiveViewInfo
$baseViewId = [int]$active.viewId
Assert-BaseViewHasStructContent -ViewId $baseViewId -ViewName $active.name

Write-Host ("[Base] viewId={0} name='{1}'" -f $baseViewId, $active.name) -ForegroundColor Cyan

# 1) Structural frames + tags
$framesViewId = Duplicate-View -ViewId $baseViewId -Suffix 'StructFrames'
Isolate-StructFraming -ViewId $framesViewId
Export-ViewToDwg -ViewId $framesViewId -FileName 'StructFrames'

# 2) Structural columns + tags
$columnsViewId = Duplicate-View -ViewId $baseViewId -Suffix 'StructColumns'
Isolate-StructColumns -ViewId $columnsViewId
Export-ViewToDwg -ViewId $columnsViewId -FileName 'StructColumns'

# 3) Grids
$gridsViewId = Duplicate-View -ViewId $baseViewId -Suffix 'Grids'
Isolate-Grids -ViewId $gridsViewId
Export-ViewToDwg -ViewId $gridsViewId -FileName 'Grids'

Write-Host "[Done] Exported DWGs for StructFrames / StructColumns / Grids." -ForegroundColor Green


