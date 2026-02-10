<#
.SYNOPSIS
  Bridge script between Codex GUI and the actual Codex CLI or backend.

.DESCRIPTION
  The WPF Codex GUI calls this script for each prompt.
  - The prompt text is stored in a UTF-8 file and passed via -PromptFile.
  - SessionId is a GUI-managed identifier; you can map it to a real Codex session.
  - Model is an optional model name (Codex or local backend specific).
  - ShowStatus can be used to request a status response instead of a normal reply.
  - Backend chooses which engine to call (codex / local-llm / local-llm-gguf).

.PARAMETER PromptFile
  Path to a UTF-8 text file that contains the user's prompt.

.PARAMETER SessionId
  GUI session ID. You can use this to map to a Codex session ID or history.

.PARAMETER Model
  Optional model name.
  - For Backend=codex: Codex model (例: gpt-4.1, gpt-4.1-mini)
  - For Backend=local-llm: HuggingFace モデル名
  - For Backend=local-llm-gguf: gguf ファイルパス (省略可)

.PARAMETER ShowStatus
  When set, the call should return Codex's current status instead of (or
  in addition to) answering the user prompt.

.PARAMETER Backend
  Backend engine to use:
    - codex           : Codex CLI (従来動作)
    - local-llm       : local_llm.py (Transformers + PyTorch)
    - local-llm-gguf  : local_llm_gguf.py (gguf + llama-cpp-python)
#>

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$PromptFile,

  [string]$SessionId,

  [string]$Model,

  [string]$ReasoningEffort,

  [string]$Profile,

  [string[]]$ImagePaths,

  [switch]$ShowStatus,

  [ValidateSet("codex", "local-llm", "local-llm-gguf")]
  [string]$Backend = "codex"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try { chcp 65001 > $null } catch {}
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}
try { if ($PSVersionTable.PSVersion.Major -ge 7) { $PSNativeCommandUseErrorActionPreference = $false } } catch {}

if (-not (Test-Path -LiteralPath $PromptFile -PathType Leaf)) {
  throw "Prompt file not found: $PromptFile"
}

$prompt = Get-Content -LiteralPath $PromptFile -Raw -Encoding UTF8

if (-not $SessionId) {
  $SessionId = 'default'
}

function Get-SessionMapPath {
  $root = Join-Path $env:USERPROFILE '.codex'
  if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    [void](New-Item -ItemType Directory -Path $root -Force)
  }
  return (Join-Path $root 'codex_gui_sessions.json')
}

function Load-SessionMap {
  $path = Get-SessionMapPath
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    return @{}
  }
  try {
    $json = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    if (-not $json -or -not $json.Trim()) {
      return @{}
    }
    # PowerShell 7 以外でも動作するように、ハッシュテーブルに変換
    $obj = $json | ConvertFrom-Json
    if ($obj -is [System.Collections.IDictionary]) {
      return [hashtable]$obj
    }
    $ht = @{}
    $obj.PSObject.Properties | ForEach-Object {
      $ht[$_.Name] = $_.Value
    }
    return $ht
  } catch {
    Write-Warning "Failed to load Codex GUI session map: $($_.Exception.Message)"
    return @{}
  }
}

function Save-SessionMap([hashtable]$map) {
  $path = Get-SessionMapPath
  try {
    $json = $map | ConvertTo-Json -Depth 6
    Set-Content -LiteralPath $path -Value $json -Encoding UTF8
  } catch {
    Write-Warning "Failed to save Codex GUI session map: $($_.Exception.Message)"
  }
}

function Extract-SessionIdFromOutput([string]$text) {
  if (-not $text) { return $null }
  $m = [regex]::Match($text, 'session id:\s*([0-9a-f-]+)', 'IgnoreCase')
  if ($m.Success) { return $m.Groups[1].Value } else { return $null }
}

function Get-RepoRoot {
  # 1) 環境変数 REVIT_MCP_ROOT があれば最優先（Revit_MCP ルート）
  if ($env:REVIT_MCP_ROOT -and (Test-Path -LiteralPath $env:REVIT_MCP_ROOT -PathType Container)) {
    $c = Join-Path $env:REVIT_MCP_ROOT 'Codex'
    if (Test-Path -LiteralPath $c -PathType Container) {
      return (Resolve-Path -LiteralPath $c).Path
    }
  }

  # 2) 環境変数 CODEX_MCP_ROOT があれば次に優先（Codex 直下）
  if ($env:CODEX_MCP_ROOT -and (Test-Path -LiteralPath $env:CODEX_MCP_ROOT -PathType Container)) {
    return (Resolve-Path -LiteralPath $env:CODEX_MCP_ROOT).Path
  }

  # 3) paths.json（%LOCALAPPDATA%\RevitMCP\paths.json）を読む
  try {
    $paths = Join-Path $env:LOCALAPPDATA 'RevitMCP\paths.json'
    if (Test-Path -LiteralPath $paths -PathType Leaf) {
      $cfg = Get-Content -LiteralPath $paths -Raw -Encoding UTF8 | ConvertFrom-Json
      if ($cfg.codexRoot -and (Test-Path -LiteralPath $cfg.codexRoot -PathType Container)) {
        return (Resolve-Path -LiteralPath $cfg.codexRoot).Path
      }
      if ($cfg.root) {
        $c2 = Join-Path $cfg.root 'Codex'
        if (Test-Path -LiteralPath $c2 -PathType Container) {
          return (Resolve-Path -LiteralPath $c2).Path
        }
      }
    }
  } catch {}

  # 4) 既定: ユーザーの Documents\Revit_MCP\Codex → Documents\Codex_MCP\Codex
  $docs = [Environment]::GetFolderPath('MyDocuments')
  if ($docs) {
    $fallbackRoot = Join-Path $docs 'Revit_MCP\Codex'
    if (Test-Path -LiteralPath $fallbackRoot -PathType Container) {
      return (Resolve-Path -LiteralPath $fallbackRoot).Path
    }
    $legacyRoot = Join-Path $docs 'Codex_MCP\Codex'
    if (Test-Path -LiteralPath $legacyRoot -PathType Container) {
      return (Resolve-Path -LiteralPath $legacyRoot).Path
    }
  }

  # 3) 最後のフォールバック: この GUI の一つ上のフォルダ
  return (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

function Set-RevitMcpEnvFromPaths {
  try {
    $paths = Join-Path $env:LOCALAPPDATA 'RevitMCP\\paths.json'
    if (Test-Path -LiteralPath $paths -PathType Leaf) {
      $cfg = Get-Content -LiteralPath $paths -Raw -Encoding UTF8 | ConvertFrom-Json
      if ($cfg.root -and (Test-Path -LiteralPath $cfg.root -PathType Container)) {
        $env:REVIT_MCP_ROOT = (Resolve-Path -LiteralPath $cfg.root).Path
      }
      if ($cfg.workRoot -and (Test-Path -LiteralPath $cfg.workRoot -PathType Container)) {
        $env:REVIT_MCP_WORK_ROOT = (Resolve-Path -LiteralPath $cfg.workRoot).Path
      }
    }
  } catch {
    # ignore
  }
}

$map = Load-SessionMap

function Test-ProfileAvailable {
  param([string]$ProfileName)
  if (-not $ProfileName) { return $false }
  $cfg = Join-Path $env:USERPROFILE '.codex\config.toml'
  if (-not (Test-Path -LiteralPath $cfg -PathType Leaf)) { return $false }
  try {
    $lines = Get-Content -LiteralPath $cfg -Encoding UTF8
    $escaped = [regex]::Escape($ProfileName)
    foreach ($line in $lines) {
      if ($line -match "^\s*\[(profiles|profile)\.$escaped\]\s*$") { return $true }
      if ($line -match "^\s*\[profile\s+`"$escaped`"\]\s*$") { return $true }
    }
  } catch {
    return $false
  }
  return $false
}

function Ensure-Hashtable([object]$obj) {
  if ($null -eq $obj) { return @{} }
  if ($obj -is [System.Collections.IDictionary]) { return [hashtable]$obj }
  $ht = @{}
  try {
    $obj.PSObject.Properties | ForEach-Object {
      $ht[$_.Name] = $_.Value
    }
  } catch {
    # ignore
  }
  return $ht
}

# Model is authoritative when explicitly passed:
# - non-empty: set model
# - empty/whitespace: clear model (use backend default)
$hasModelParam = $PSBoundParameters.ContainsKey('Model')
$modelNormalized = $null
if ($hasModelParam) {
  $m = $Model
  if ($m) { $m = $m.Trim() }
  if ($m) { $modelNormalized = $m } else { $modelNormalized = $null }
}

$hasEffortParam = $PSBoundParameters.ContainsKey('ReasoningEffort')
$effortNormalized = $null
if ($hasEffortParam) {
  $e = $ReasoningEffort
  if ($e) { $e = $e.Trim() }
  if ($e) { $effortNormalized = $e } else { $effortNormalized = $null }
}

if (-not $map.ContainsKey($SessionId)) {
  $map[$SessionId] = @{
    codexSessionId = $null
    model          = $modelNormalized
    reasoning_effort = $effortNormalized
    profile        = $null
    createdAt      = (Get-Date).ToString('o')
    lastUsedAt     = $null
  }
} else {
  # Normalize legacy entries loaded as PSCustomObject so we can add new keys safely.
  $map[$SessionId] = Ensure-Hashtable $map[$SessionId]
  if ($hasModelParam) {
    $map[$SessionId].model = $modelNormalized
  }
  if ($hasEffortParam) {
    $map[$SessionId].reasoning_effort = $effortNormalized
  }
}

if ($PSBoundParameters.ContainsKey('Profile')) {
  $p = $Profile
  if ($p) { $p = $p.Trim() }
  if ($p) { $map[$SessionId].profile = $p } else { $map[$SessionId].profile = $null }
}

$entry = $map[$SessionId]
$codexSessionId = $entry.codexSessionId

if ($ShowStatus) {
  $url = 'https://chatgpt.com/codex/settings/usage'
  try {
    Start-Process $url | Out-Null
    Write-Output "ブラウザで Codex Usage ページを開きました: $url"
  } catch {
    Write-Output "Usage ページを開こうとしてエラーが発生しました: $url"
    Write-Output $_.Exception.Message
  }
  return
}

$entry.lastUsedAt = (Get-Date).ToString('o')

switch ($Backend) {
  'codex' {
    # --- Codex CLI backend（従来動作） ---
    $repoRoot = Get-RepoRoot
    Set-RevitMcpEnvFromPaths
    try {
      if (-not $env:REVIT_MCP_ROOT -and $repoRoot) {
        # If repoRoot points to ...\Codex, use its parent as Revit_MCP root.
        $rootCandidate = $repoRoot
        if ($rootCandidate -match '\\Codex$') {
          $rootCandidate = Split-Path -Parent $rootCandidate
        }
        if ($rootCandidate -and (Test-Path -LiteralPath $rootCandidate -PathType Container)) {
          $env:REVIT_MCP_ROOT = (Resolve-Path -LiteralPath $rootCandidate).Path
        }
      }
      if (-not $env:REVIT_MCP_WORK_ROOT -and $env:REVIT_MCP_ROOT) {
        $wr = Join-Path $env:REVIT_MCP_ROOT 'Projects'
        if (Test-Path -LiteralPath $wr -PathType Container) {
          $env:REVIT_MCP_WORK_ROOT = (Resolve-Path -LiteralPath $wr).Path
        }
      }
    } catch {
      # ignore
    }

    $tokens = @(
      '--yolo',
      'exec',
      '--dangerously-bypass-approvals-and-sandbox',
      '--sandbox', 'danger-full-access',
      '--color', 'never',
      '--skip-git-repo-check'
    )
    if ($entry.model) {
      $tokens += @('--model', $entry.model)
    }
    if ($entry.reasoning_effort) {
      # Override Codex reasoning effort (TOML string). Avoid quoting to prevent parse errors.
      $tokens += @('--config', ('model_reasoning_effort={0}' -f $entry.reasoning_effort))
    }
    if ($entry.profile) {
      if (Test-ProfileAvailable $entry.profile) {
        $tokens += @('--profile', $entry.profile)
      } else {
        Write-Warning "Profile '$($entry.profile)' not found in config.toml. Ignoring --profile."
      }
    }

    # Optional image attachments (consent-gated by GUI; still validate paths here).
    $images = @()
    if ($ImagePaths) {
      foreach ($p in $ImagePaths) {
        if (-not $p) { continue }
        $pp = $p.Trim()
        if (-not $pp) { continue }
        if (Test-Path -LiteralPath $pp -PathType Leaf) {
          $images += $pp
        }
      }
    }
    if ($images.Count -gt 0) {
      $tokens += '--image'
      $tokens += $images
    }

    $tokens += '-'
    if ($codexSessionId) {
      $tokens += @('resume', $codexSessionId)
    }

    $tempOut = Join-Path $env:TEMP ("codex_gui_out_{0}.txt" -f ([System.Guid]::NewGuid().ToString('N')))
    Push-Location $repoRoot
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
      if ($prompt) {
        # Codex の出力をファイルに保存しつつ、そのまま標準出力にも流す
        $prompt | & codex @tokens 2>&1 | Tee-Object -FilePath $tempOut
      } else {
        & codex @tokens 2>&1 | Tee-Object -FilePath $tempOut
      }
      $exit = $LASTEXITCODE
    } finally {
      $ErrorActionPreference = $prevEap
      Pop-Location
    }

    try {
      if (Test-Path -LiteralPath $tempOut -PathType Leaf) {
        $allOut = Get-Content -LiteralPath $tempOut -Raw -Encoding UTF8
        $newSessionId = Extract-SessionIdFromOutput $allOut
        if (-not $codexSessionId -and $newSessionId) {
          $entry.codexSessionId = $newSessionId
        }
      }
    } catch {
      Write-Warning "Failed to parse Codex session id: $($_.Exception.Message)"
    } finally {
      try {
        if (Test-Path -LiteralPath $tempOut -PathType Leaf) {
          Remove-Item -LiteralPath $tempOut -Force -ErrorAction SilentlyContinue
        }
      } catch {}
    }

    Save-SessionMap $map

    if ($exit -ne 0) {
      exit $exit
    }
  }

  'local-llm' {
    # --- HuggingFace Transformers ベースのローカル LLM backend ---
    $llmRoot = Resolve-Path (Join-Path $PSScriptRoot '..\local_llm')
    $pythonExe = 'python'
    Push-Location $llmRoot
    try {
      $args = @('.\local_llm.py', '--device', 'auto', '--max-new-tokens', '256')
      if ($Model) {
        $args += @('--model', $Model)
      }
      $args += @('--prompt', $prompt)

      & $pythonExe @args 2>&1
      $exit = $LASTEXITCODE
    } finally {
      Pop-Location
    }

    Save-SessionMap $map
    if ($exit -ne 0) {
      exit $exit
    }
  }

  'local-llm-gguf' {
    # --- gguf + llama-cpp-python ベースのローカル LLM backend ---
    $llmRoot = Resolve-Path (Join-Path $PSScriptRoot '..\local_llm')
    $pythonExe = 'python'
    Push-Location $llmRoot
    try {
      $args = @('.\local_llm_gguf.py', '--device', 'auto', '--max-tokens', '256')
      if ($Model) {
        # Model パラメータが指定されていれば gguf パスとして扱う
        $args += @('--model-path', $Model)
      }
      $args += @('--prompt', $prompt)

      & $pythonExe @args 2>&1
      $exit = $LASTEXITCODE
    } finally {
      Pop-Location
    }

    Save-SessionMap $map
    if ($exit -ne 0) {
      exit $exit
    }
  }

  default {
    Write-Error "Unknown backend: $Backend"
    exit 1
  }
}

