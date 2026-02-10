<#
.SYNOPSIS
  Get Revit project info from MCP on port 5210 (or custom port).

.DESCRIPTION
  Calls the Revit MCP JSON-RPC endpoint `get_project_info` on the specified port,
  then saves the full JSON result under the repository Work folder:

    Projects\\RevitMcp\<Port>\project_info.json

  The script writes the output file path to stdout on success.

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

# Durable client (recommended) – avoids dealing with jobId / polling here.
$pythonClient = Join-Path $repoRoot "Scripts\\Manuals\send_revit_command_durable.py"
if (-not (Test-Path -LiteralPath $pythonClient -PathType Leaf)) {
  throw "Durable Revit client not found: $pythonClient"
}

$workRoot = Join-Path $repoRoot "Work"
if (-not (Test-Path -LiteralPath $workRoot -PathType Container)) {
  throw "Work folder not found at: $workRoot"
}

$portRoot = Join-Path $workRoot "RevitMcp\$Port"
New-Item -ItemType Directory -Path $portRoot -Force | Out-Null

$outPath = Join-Path $portRoot "project_info.json"

# Call durable client to fetch get_project_info and write JSON to $outPath
python $pythonClient --port $Port --command get_project_info --output-file $outPath | Out-Null
if (-not (Test-Path -LiteralPath $outPath -PathType Leaf)) {
  throw "Revit MCP durable client did not produce expected file: $outPath"
}

# GUI 側からは相対パスで扱いやすいように、Work からの相対パスを返す
$relative = Join-Path "Work" (Join-Path "RevitMcp" (Join-Path $Port "project_info.json"))
Write-Output $relative


