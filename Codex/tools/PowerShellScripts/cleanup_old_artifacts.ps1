param(
  [int]$Days = 7,
  [switch]$Execute,
  [switch]$IncludeLocalRevitMcp = $true,
  [switch]$IncludeRepoWork = $true,
  [string]$RepoRoot = $PSScriptRoot | Split-Path -Parent
)

$ErrorActionPreference = "Stop"

function Get-NewestWriteTime([string]$Path) {
  $newest = (Get-Item -LiteralPath $Path -Force).LastWriteTime
  Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.LastWriteTime -gt $newest) { $newest = $_.LastWriteTime }
  }
  return $newest
}

function Remove-OldFile([string]$Path, [datetime]$Threshold, [ref]$DeletedCount, [ref]$DeletedBytes) {
  try {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if (-not $item) { return }
    if ($item.PSIsContainer) { return }
    if ($item.LastWriteTime -ge $Threshold) { return }
    if (-not $Execute) {
      Write-Host "[DRY] file  $Path"
      return
    }
    $len = 0
    try { $len = [int64]$item.Length } catch { $len = 0 }
    Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    $DeletedCount.Value++
    $DeletedBytes.Value += $len
    Write-Host "[DEL] file  $Path"
  } catch {
    Write-Warning "Delete file failed: $Path :: $($_.Exception.Message)"
  }
}

function Remove-OldDir([string]$Dir, [datetime]$Threshold, [ref]$DeletedCount) {
  try {
    if (-not (Test-Path -LiteralPath $Dir)) { return }
    $newest = Get-NewestWriteTime $Dir
    if ($newest -ge $Threshold) { return }
    if (-not $Execute) {
      Write-Host "[DRY] dir   $Dir"
      return
    }
    Remove-Item -LiteralPath $Dir -Recurse -Force -ErrorAction SilentlyContinue
    $DeletedCount.Value++
    Write-Host "[DEL] dir   $Dir"
  } catch {
    Write-Warning "Delete dir failed: $Dir :: $($_.Exception.Message)"
  }
}

if ($Days -lt 1) { $Days = 7 }
$threshold = (Get-Date).AddDays(-$Days)
Write-Host "Threshold: $threshold (Days=$Days)  Execute=$Execute"

$deletedFiles = 0
$deletedDirs = 0
$deletedBytes = [int64]0

if ($IncludeLocalRevitMcp) {
  $localRoot = Join-Path $env:LOCALAPPDATA "RevitMCP"
  Write-Host "LocalRoot: $localRoot"
  if (Test-Path -LiteralPath $localRoot) {
    # logs/locks
    foreach ($dir in @("logs","locks")) {
      $p = Join-Path $localRoot $dir
      if (Test-Path -LiteralPath $p) {
        Get-ChildItem -LiteralPath $p -File -Force -ErrorAction SilentlyContinue | ForEach-Object {
          Remove-OldFile $_.FullName $threshold ([ref]$deletedFiles) ([ref]$deletedBytes)
        }
      }
    }

    # server_state_*.json + *.bak_* + *.tmp
    Get-ChildItem -LiteralPath $localRoot -File -Force -ErrorAction SilentlyContinue | ForEach-Object {
      $name = $_.Name
      if ($name -match '^server_state_.*\.json$') { Remove-OldFile $_.FullName $threshold ([ref]$deletedFiles) ([ref]$deletedBytes); return }
      if ($name -match '\.bak_' ) { Remove-OldFile $_.FullName $threshold ([ref]$deletedFiles) ([ref]$deletedBytes); return }
      if ($name -match '\.tmp(\.json)?$') { Remove-OldFile $_.FullName $threshold ([ref]$deletedFiles) ([ref]$deletedBytes); return }
    }

    # data snapshots
    $dataDir = Join-Path $localRoot "data"
    if (Test-Path -LiteralPath $dataDir) {
      Get-ChildItem -LiteralPath $dataDir -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object {
        Remove-OldDir $_.FullName $threshold ([ref]$deletedDirs)
      }
    }

    # queue per-port dirs (do not touch shared root DBs)
    $queueDir = Join-Path $localRoot "queue"
    if (Test-Path -LiteralPath $queueDir) {
      Get-ChildItem -LiteralPath $queueDir -Directory -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^p\\d+$' } | ForEach-Object {
        Remove-OldDir $_.FullName $threshold ([ref]$deletedDirs)
      }
    }
  }
}

if ($IncludeRepoWork) {
  $workRoot = Join-Path $RepoRoot "Work"
  Write-Host "Repo Work: $workRoot"
  if (Test-Path -LiteralPath $workRoot) {
    # Default: clean common cache-like subdirs under Work
    foreach ($sub in @("RevitMcp","build_out","build_out_RevitMCPAddin_real","build_out\\RevitMCPAddin_real")) {
      $p = Join-Path $workRoot $sub
      if (Test-Path -LiteralPath $p) {
        Get-ChildItem -LiteralPath $p -File -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
          Remove-OldFile $_.FullName $threshold ([ref]$deletedFiles) ([ref]$deletedBytes)
        }
        # Remove empty dirs (best-effort)
        Get-ChildItem -LiteralPath $p -Directory -Recurse -Force -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | ForEach-Object {
          try {
            if (-not (Get-ChildItem -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue)) {
              if ($Execute) { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue; $deletedDirs++ }
              else { Write-Host "[DRY] emptydir $_" }
            }
          } catch { }
        }
      }
    }
  }
}

Write-Host "Done. deletedFiles=$deletedFiles deletedDirs=$deletedDirs deletedBytes=$deletedBytes"

