[CmdletBinding()]
param(
  [string]$BaseUrl = 'http://localhost:5215',
  [string]$ExcelPath,
  [string]$SheetName = 'Sheet1',
  [double]$Threshold = 3.0
)

<#
.SYNOPSIS
  1行目以外で、F列の値がしきい値以下の行について、
  A～J列のセル背景色を白にします（ExcelMCP + COM経由）。

.DESCRIPTION
  - /com/read_cells で F2:F<MaxRowGuess> の値を読み取り、
  - 値 <= Threshold の行に対して /com/format_range で A:J を白背景にします。

  実行前に:
    1) 対象のブックを Excel で開く
    2) Codex フォルダで connect_excelmcp.ps1 を実行し、ExcelMCP を起動

.PARAMETER BaseUrl
  ExcelMCP のベースURL（既定: http://localhost:5215）。

.PARAMETER ExcelPath
  対象ブックのフルパス（例: C:\path\sample.xlsx）。WorkbookFullName として使います。

.PARAMETER SheetName
  対象シート名（既定: Sheet1）。

.PARAMETER Threshold
  「F列の値 <= Threshold」の行を対象にします（既定: 3.0）。

.EXAMPLE
  pwsh -ExecutionPolicy Bypass -File .\excelmcp_clear_bg_A_to_J_when_F_le3.ps1 `
    -ExcelPath 'C:\path\sample.xlsx' -SheetName 'Sheet1'
#>

if (-not $ExcelPath) { throw 'ExcelPath is required.' }

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try { chcp 65001 > $null } catch {}

$excelUrl = $BaseUrl.TrimEnd('/')
Write-Host "[ExcelMCP] BaseUrl   = $excelUrl" -ForegroundColor Cyan
Write-Host "[ExcelMCP] ExcelPath = $ExcelPath" -ForegroundColor Cyan
Write-Host "[ExcelMCP] SheetName = $SheetName, Threshold <= $Threshold" -ForegroundColor Cyan

# 1) Health check
$healthOk = $false
try {
  $h = Invoke-RestMethod -Method GET -Uri "$excelUrl/health" -TimeoutSec 2
  if ($h.ok) { $healthOk = $true }
} catch {}
if (-not $healthOk) {
  throw "ExcelMCP server is not healthy at $excelUrl. Run connect_excelmcp.ps1 first."
}

# 2) Read F column values via COM (avoid file locks)
$maxRowGuess = 5000
$range = "F2:F$maxRowGuess"
Write-Host "[ExcelMCP] Reading range: $range" -ForegroundColor Cyan

$readBody = @{
  workbookFullName = $ExcelPath
  sheetName        = $SheetName
  rangeA1          = $range
  useValue2        = $true
} | ConvertTo-Json -Compress

$read = Invoke-RestMethod -Method Post -Uri "$excelUrl/com/read_cells" -ContentType 'application/json' -Body $readBody
if (-not $read.ok) {
  throw "com/read_cells failed: $($read.msg)"
}

$rows = @($read.rows)
$targetRows = @()

for ($i = 0; $i -lt $rows.Count; $i++) {
  $line = $rows[$i]
  if (-not $line) { continue }
  $v = $line[0]
  if ($null -eq $v) { continue }

  $valStr = $v.ToString()
  [double]$num = 0
  if (-not [double]::TryParse($valStr, [ref]$num)) { continue }

  if ($num -le $Threshold) {
    $rowIndex = 2 + $i
    $targetRows += $rowIndex
  }
}

if ($targetRows.Count -eq 0) {
  Write-Host "[Info] No rows where F <= $Threshold were found." -ForegroundColor Yellow
  return
}

Write-Host "[ExcelMCP] Formatting $($targetRows.Count) rows (A:J) with white background..." -ForegroundColor Cyan

foreach ($r in $targetRows) {
  $targetRange = "A${r}:J${r}"
  $fmtBody = @{
    workbookFullName = $ExcelPath
    sheetName        = $SheetName
    target           = $targetRange
    fillColor        = '#FFFFFF'
  } | ConvertTo-Json -Compress

  $fmt = Invoke-RestMethod -Method Post -Uri "$excelUrl/com/format_range" -ContentType 'application/json' -Body $fmtBody
  if (-not $fmt.ok) {
    Write-Host ("[Warn] format_range failed at {0}: {1}" -f $targetRange, $fmt.msg) -ForegroundColor Yellow
  }
}

Write-Host ("[Done] Updated {0} data rows." -f $targetRows.Count) -ForegroundColor Green

