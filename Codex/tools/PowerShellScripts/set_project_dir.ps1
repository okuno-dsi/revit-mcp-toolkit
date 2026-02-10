# @feature: Persist for other tools reading it later (optional) | keywords: misc
param(
  [string]$Name = 'ActiveProject'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null

$ROOT = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$proj = Join-Path $ROOT (Join-Path 'Work' $Name)
New-Item -ItemType Directory -Force -Path $proj | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $proj 'Logs') | Out-Null

$env:MCP_PROJECT_DIR = (Resolve-Path $proj).Path
# Persist for other tools reading it later (optional)
Set-Content -LiteralPath (Join-Path $ROOT 'Projects\\.project_dir') -Encoding UTF8 -Value $env:MCP_PROJECT_DIR

Write-Host ("Project directory set: " + $env:MCP_PROJECT_DIR) -ForegroundColor Green



