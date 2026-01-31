# @feature: dump struct view categories | keywords: 柱, 梁, スペース, ビュー
param(
  [int]$Port = 5210
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null | Out-Null
$env:PYTHONUTF8 = '1'

$scriptDir = $PSScriptRoot
$py = Join-Path $scriptDir 'send_revit_command_durable.py'

function Invoke-Mcp {
  param(
    [string]$Method,
    [hashtable]$Params,
    [int]$Wait = 300,
    [int]$JobTimeout = 900
  )
  $payloadJson = $Params | ConvertTo-Json -Depth 30 -Compress
  $tmp = [System.IO.Path]::GetTempFileName()
  $args = @('--port', $Port, '--command', $Method, '--params', $payloadJson, '--wait-seconds', [string]$Wait, '--output-file', $tmp)
  if ($JobTimeout -gt 0) { $args += @('--timeout-sec', [string]$JobTimeout) }
  & python -X utf8 $py @args | Out-Null
  $code = $LASTEXITCODE
  $txt = ''
  try { $txt = Get-Content -LiteralPath $tmp -Raw -Encoding UTF8 } catch {}
  if (Test-Path $tmp) { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
  if ($code -ne 0) { throw "MCP call failed ($Method): $txt" }
  if ([string]::IsNullOrWhiteSpace($txt)) { throw "Empty response from MCP ($Method)" }
  return ($txt | ConvertFrom-Json -Depth 200)
}

function GetPayload($obj){
  if($null -eq $obj){ return $null }
  if($obj.result -and $obj.result.result){ return $obj.result.result }
  if($obj.result){ return $obj.result }
  return $obj
}

# get all views (non-templates, detailed)
$viewsResp = Invoke-Mcp -Method 'get_views' -Params @{ includeTemplates=$false; detail=$true }
$views = @(GetPayload $viewsResp).views

# pick the latest StructFrames/StructColumns/Grids views (by viewId)
$targets = $views | Where-Object {
  $_.name -like 'StructFrames*' -or
  $_.name -like 'StructColumns*' -or
  $_.name -like 'Grids*'
} | Sort-Object viewId -Descending

if(-not $targets){
  Write-Host "No StructFrames/StructColumns/Grids views found." -ForegroundColor Yellow
  exit 0
}

foreach($v in $targets){
  $vid = [int]$v.viewId
  $name = [string]$v.name
  Write-Host "=== View: $name (id=$vid) ===" -ForegroundColor Cyan

  # get elements in view (idsOnly=false to allow category information)
  $ievResp = Invoke-Mcp -Method 'get_elements_in_view' -Params @{
    viewId = $vid
    _shape = @{ idsOnly = $false; page = @{ limit = 5000 } }
    _filter = @{ modelOnly = $false; excludeImports = $true }
  }
  $iev = GetPayload $ievResp

  $rows = @()
  if($iev.rows){ $rows = @($iev.rows) }
  elseif($iev.elements){ $rows = @($iev.elements) }

  $catIds = @()
  foreach($r in $rows){
    try{
      if($r.categoryId -ne $null){
        $catIds += [int]$r.categoryId
      }
    }catch{}
  }
  $catIds = $catIds | Sort-Object -Unique

  if(-not $catIds){
    Write-Host "  (No categoryIds resolved from sample.)" -ForegroundColor DarkYellow
  } else {
    Write-Host ("  Visible categoryIds (sample): {0}" -f ($catIds -join ', '))
  }
}

