# @feature: test failure handling overlapping wall | keywords: 壁, スペース, レベル
<#
.SYNOPSIS
  Test failureHandling behavior with a reproducible Revit warning.

.DESCRIPTION
  Creates a wall, then tries to create an overlapping wall on the same line.
  The second wall should usually trigger an overlap warning. With failureHandling
  enabled (mode=rollback), the command should return TX_NOT_COMMITTED and no model
  changes should be applied for the second attempt.

  After the test, the first wall is deleted for cleanup.

.PARAMETER Port
  Revit MCP port (default: 5210)

.PARAMETER BaseLevelName
  Optional level name to use (e.g. "1FL"). If omitted, create_wall falls back to the first level in the model.

.PARAMETER X0Mm
  Start X in mm (default: 300000)

.PARAMETER Y0Mm
  Start Y in mm (default: 300000)

.PARAMETER LengthMm
  Wall length in mm (default: 5000)

.EXAMPLE
  pwsh -ExecutionPolicy Bypass -File ./..\..\..\Docs\..\\..\\Manuals/test_failure_handling_overlapping_wall.ps1 -Port 5210
#>

[CmdletBinding()]
param(
  [int]$Port = 5210,
  [string]$BaseLevelName = "",
  [double]$X0Mm = 300000,
  [double]$Y0Mm = 300000,
  [double]$LengthMm = 5000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try { chcp 65001 > $null } catch {}

function Invoke-RevitMcp {
  param(
    [string]$Command,
    [hashtable]$Params
  )

  $paramsJson = ($Params | ConvertTo-Json -Depth 20 -Compress)
  $scriptPath = Join-Path -Path $PSScriptRoot -ChildPath 'send_revit_command_durable.py'

  $tmp = Join-Path -Path $env:TEMP -ChildPath ("revitmcp_{0}_{1}.json" -f $Command, (Get-Date -Format 'yyyyMMdd_HHmmssfff'))
  $raw = & python $scriptPath --port $Port --command $Command --params $paramsJson --wait-seconds 60 --output-file $tmp 2>&1 | Out-String

  if (-not (Test-Path -Path $tmp -PathType Leaf)) {
    throw "RevitMCP did not write output file: $tmp`nRaw output:`n$raw"
  }

  try {
    $json = Get-Content -Raw -Encoding UTF8 -Path $tmp
    return ($json | ConvertFrom-Json)
  } catch {
    throw "Failed to parse JSON from '$Command'. SavedTo=$tmp`nRaw output:`n$raw"
  }
}

$wallId = 0
$wallId2 = 0

$baseParams = @{
  start = @{ x = $X0Mm; y = $Y0Mm; z = 0 }
  end   = @{ x = ($X0Mm + $LengthMm); y = $Y0Mm; z = 0 }
}
if (-not [string]::IsNullOrWhiteSpace($BaseLevelName)) {
  $baseParams.baseLevelName = $BaseLevelName
}

try {
  Write-Host "[1/3] create_wall (baseline)" -ForegroundColor Cyan
  $r1 = Invoke-RevitMcp -Command 'create_wall' -Params $baseParams
  try { $wallId = [int]($r1.result.result.elementId) } catch { $wallId = 0 }
  Write-Host ("  wallId={0}" -f $wallId) -ForegroundColor Green

  Write-Host "[2/3] create_wall (overlap attempt, failureHandling=rollback)" -ForegroundColor Cyan
  $r2Params = @{}
  $baseParams.GetEnumerator() | ForEach-Object { $r2Params[$_.Key] = $_.Value }
  $r2Params.failureHandling = @{ enabled = $true; mode = 'rollback' }

  $r2 = Invoke-RevitMcp -Command 'create_wall' -Params $r2Params
  Write-Host ($r2 | ConvertTo-Json -Depth 20) -ForegroundColor Gray
  try { $wallId2 = [int]($r2.result.result.elementId) } catch { $wallId2 = 0 }
}
finally {
  Write-Host "[3/3] cleanup: delete_wall" -ForegroundColor Cyan
  if ($wallId2 -gt 0 -and $wallId2 -ne $wallId) {
    Write-Host ("  delete second wallId={0}" -f $wallId2) -ForegroundColor Yellow
    try {
      $r3b = Invoke-RevitMcp -Command 'delete_wall' -Params @{ elementId = [int]$wallId2 }
      Write-Host ($r3b | ConvertTo-Json -Depth 20) -ForegroundColor Gray
    } catch {
      Write-Warning "Failed to delete second wallId=$wallId2 : $($_.Exception.Message)"
    }
  }
  if ($wallId -gt 0) {
    try {
      $r3 = Invoke-RevitMcp -Command 'delete_wall' -Params @{ elementId = [int]$wallId }
      Write-Host ($r3 | ConvertTo-Json -Depth 20) -ForegroundColor Gray
    } catch {
      Write-Warning "Failed to delete wallId=$wallId : $($_.Exception.Message)"
    }
  } else {
    Write-Warning "Skip cleanup: wallId is missing."
  }
}

Write-Host "Done." -ForegroundColor Cyan


