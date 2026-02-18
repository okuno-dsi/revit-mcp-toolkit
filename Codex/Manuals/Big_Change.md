# Big_Change (Revit_MCP 構成移行・パス解決強化)

## 概要
- ルートを `Codex_MCP` から `Revit_MCP` へ移行し、`Apps / Codex / Docs / Scripts / Projects / Logs / Settings` に整理。
- ルート解決を `paths.json`（%LOCALAPPDATA%\RevitMCP\paths.json）優先に統一。
- RevitMCPAddin の起動/保存先が新構成を参照するように更新。

## 詳述
### 1) フォルダ構成の変更（運用ルール）
- 新ルート: `C:\Users\<user>\Documents\Revit_MCP`
- 主要フォルダ
  - `Apps`    : 実行系（CodexGUI / AutoCAD / Excel 等）
  - `Codex`   : Codex/Agent の実行ベース（GUI 連携、ガイド、ログ参照）
  - `Docs`    : マニュアル・設計書（文書のみ）
- `Scripts` : 共有スクリプト（PythonRunnerScripts / Tools / Reference 等）
  - `Projects`: プロジェクト別作業データ（docKey 付き）
  - `Logs`    : GUI ログ等の共通ログ（例: `Logs/CodexGUI_Log`）
  - `Settings`: GUI/環境設定（例: `Settings/CodexGUI`）
- Codex 本体は `Revit_MCP\Codex` に集約（`Docs\Codex` は廃止し、`Docs\Manuals` に文書を集約）
- `Docs\Manuals\Scripts` は廃止し、`Scripts\\Reference` に移動（実行系と文書を分離）
- `Docs\Manuals\Config` は廃止し、`Settings\CodexGUI` に移動
- `Docs\Manuals\Work` は廃止し、作業データは `Projects` 配下へ統一
- `Codex\GUI_Log` は `Logs\CodexGUI_Log` に移動（互換用に junction を作成）

### 2) パス解決の共通化（paths.json）
- 設定ファイル: `%LOCALAPPDATA%\RevitMCP\paths.json`
- 利用フィールド（例）
  - `root`      : `Revit_MCP` ルート
  - `codexRoot` : `Revit_MCP\Codex`
  - `workRoot`  : `Revit_MCP\Projects`（この配下に `<ProjectName>_<docKey>` を作成）
  - `appsRoot`  : `Revit_MCP\Apps`
- 旧構成（Codex_MCP）もフォールバックとして許容。

### 3) コード修正（RevitMCPAddin）
- `Core/Paths.cs`
  - `paths.json` 読み込みの追加
  - `ResolveRoot / ResolveAppsRoot / ResolveCodexRoot / ResolveWorkRoot` を実装
  - 旧環境変数や旧フォルダのフォールバックを維持
- `Commands/Dev/LaunchCodexGuiCommand.cs`
  - 新ルート (`Apps\CodexGui`) と `Codex` を参照するよう変更（`Docs\Codex` のフォールバックは無効化）
  - `REVIT_MCP_ROOT / REVIT_MCP_WORK_ROOT / CODEX_MCP_ROOT` を適切に設定
- `UI/PythonRunner/PythonRunnerWindow.xaml.cs`
  - Work ルート解決を `Paths.ResolveWorkRoot()` に統一
- `UI/InfoPick/InfoPickWindow.xaml.cs`
  - 保存先の Work ルート解決を `Paths.ResolveWorkRoot()` に統一
- `CodexGui/MainWindow.xaml.cs`
  - Work ルート解決を `paths.json` の `workRoot`（= `Revit_MCP\Projects`）に統一
  - GUI ログ出力先を `Logs/CodexGUI_Log` に変更

### 4) 影響範囲と運用
- 既存ユーザーの `Codex_MCP` はフォールバック対象として維持。
- `paths.json` を設置すれば明示的に新構成へ誘導可能。
- `Projects/<ProjectName>_<docKey>` を前提とする運用規約は `WORK_RULES.md` に反映。

### 5) docGuid の統一（Ledger DocKey へ移行）
- `docGuid` を **ProjectInformation.UniqueId ではなく、Ledger の ProjectToken（docKey）** に統一。
- 目的: 同一プロジェクトでフォルダが分裂する問題を解消（Pick Info と他機能の docKey を一致させる）。
- docKey が取得できない場合は従来通り UniqueId をフォールバック。

## 変更ファイル（今回）
- `%USERPROFILE%\\Documents\\Revit_MCP\\RevitMCPAddin\Core\Paths.cs`
- `%USERPROFILE%\\Documents\\Revit_MCP\\RevitMCPAddin\Commands\Dev\LaunchCodexGuiCommand.cs`
- `%USERPROFILE%\\Documents\\Revit_MCP\\RevitMCPAddin\UI\PythonRunner\PythonRunnerWindow.xaml.cs`
- `%USERPROFILE%\\Documents\\Revit_MCP\\RevitMCPAddin\UI\InfoPick\InfoPickWindow.xaml.cs`
- `%USERPROFILE%\\Documents\\Revit_MCP\\RevitMCPAddin\Core\ContextTokenService.cs`
- `%USERPROFILE%\\Documents\\Revit_MCP\\RevitMCPAddin\Core\RpcResultEnvelope.cs`
- `%USERPROFILE%\\Documents\\Revit_MCP\\RevitMCPAddin\Core\ConfirmTokenService.cs`
- `%USERPROFILE%\\Documents\\Revit_MCP\\RevitMCPAddin\Core\ExpectedContextGuard.cs`
- `%USERPROFILE%\\Documents\\Revit_MCP\\RevitMCPAddin\Core\DocumentResolver.cs`
- `%USERPROFILE%\\Documents\\Revit_MCP\\RevitMCPAddin\Commands\MetaOps\AgentBootstrapHandler.cs`
- `%USERPROFILE%\\Documents\\Revit_MCP\\RevitMCPAddin\Commands\Rpc\GetOpenDocumentsCommand.cs`
- `%USERPROFILE%\\Documents\\Revit_MCP\\Codex\Manuals\Big_Change.md`
- `%USERPROFILE%\\Documents\\Revit_MCP\\Codex\CodexGui\MainWindow.xaml.cs`





