<#
.SYNOPSIS
  Get Revit rooms info from MCP on port 5210 (or custom port).

.DESCRIPTION
  Calls the Revit MCP JSON-RPC endpoint `get_rooms` on the specified port,
  then saves the full JSON result under the repository Work folder:

    Projects\\RevitMcp\<Port>\rooms.json

  The script writes the output file path (relative to the repo root)
  to stdout on success.

.PARAMETER Port
  Revit MCP port. Defaults to 5210.

#>

[CmdletBinding()]
param(
  [string]$Port = "5210"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try { chcp 65001 > $null } catch {}
$env:PYTHONUTF8 = '1'

# Repository root is assumed to be this script's directory.
$repoRoot = $PSScriptRoot

# Rooms export helper (get_rooms + get_room_params)
$roomsExporter = Join-Path $repoRoot "Scripts\\Manuals\export_rooms_with_params.py"
if (-not (Test-Path -LiteralPath $roomsExporter -PathType Leaf)) {
  throw "Rooms export helper not found: $roomsExporter"
}

$workRoot = Join-Path $repoRoot "Work"
if (-not (Test-Path -LiteralPath $workRoot -PathType Container)) {
  throw "Work folder not found at: $workRoot"
}

$portRoot = Join-Path $workRoot "RevitMcp\$Port"
New-Item -ItemType Directory -Path $portRoot -Force | Out-Null

$outPath = Join-Path $portRoot "rooms.json"

# Collect rooms and selected parameters using helper script
python -X utf8 $roomsExporter --port $Port --max-rooms 0 --output-file $outPath | Out-Null
if (-not (Test-Path -LiteralPath $outPath -PathType Leaf)) {
  throw "Revit MCP durable client did not produce expected file: $outPath"
}

$relative = Join-Path "Work" (Join-Path "RevitMcp" (Join-Path $Port "rooms.json"))
Write-Output $relative


