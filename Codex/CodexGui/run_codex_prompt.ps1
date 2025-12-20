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

  [switch]$ShowStatus,

  [ValidateSet("codex", "local-llm", "local-llm-gguf")]
  [string]$Backend = "codex"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try { chcp 65001 > $null } catch {}
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

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
  # 1) 環境変数 CODEX_MCP_ROOT があれば最優先
  if ($env:CODEX_MCP_ROOT -and (Test-Path -LiteralPath $env:CODEX_MCP_ROOT -PathType Container)) {
    return (Resolve-Path -LiteralPath $env:CODEX_MCP_ROOT).Path
  }

  # 2) 既定: ユーザーの Documents\Codex_MCP\Codex
  $docs = [Environment]::GetFolderPath('MyDocuments')
  if ($docs) {
    $defaultRoot = Join-Path $docs 'Codex_MCP\Codex'
    if (Test-Path -LiteralPath $defaultRoot -PathType Container) {
      return (Resolve-Path -LiteralPath $defaultRoot).Path
    }
  }

  # 3) 最後のフォールバック: この GUI の一つ上のフォルダ
  return (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

$map = Load-SessionMap
if (-not $map.ContainsKey($SessionId)) {
  $map[$SessionId] = @{
    codexSessionId = $null
    model          = $Model
    createdAt      = (Get-Date).ToString('o')
    lastUsedAt     = $null
  }
} else {
  # モデル名は最新の指定で上書き（空なら前回を維持）
  if ($Model) {
    $map[$SessionId].model = $Model
  }
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
    $tokens += '-'
    if ($codexSessionId) {
      $tokens += @('resume', $codexSessionId)
    }

    $tempOut = Join-Path $env:TEMP ("codex_gui_out_{0}.txt" -f ([System.Guid]::NewGuid().ToString('N')))
    Push-Location $repoRoot
    try {
      if ($prompt) {
        # Codex の出力をファイルに保存しつつ、そのまま標準出力にも流す
        $prompt | & codex @tokens 2>&1 | Tee-Object -FilePath $tempOut
      } else {
        & codex @tokens 2>&1 | Tee-Object -FilePath $tempOut
      }
      $exit = $LASTEXITCODE
    } finally {
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

