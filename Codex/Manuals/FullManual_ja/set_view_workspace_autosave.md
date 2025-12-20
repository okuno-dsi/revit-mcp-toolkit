# set_view_workspace_autosave

- カテゴリ: UI / ビュー
- 目的: View Workspace の **自動保存（クラッシュ対策）** を設定し、必要なら自動restoreのON/OFFも切り替えます。

## 概要
自動保存が有効な場合、一定間隔でアクティブドキュメントの View Workspace を保存します:

- `%APPDATA%\\RevitMCP\\ViewWorkspace\\workspace_{doc_key}.json`

また、次のタイミングでも保存します（best-effort）:
- `DocumentClosing` / アドイン `OnShutdown`

設定は次に保存されます:
- `%LOCALAPPDATA%\\RevitMCP\\settings.json` → `viewWorkspace`

既定:
- `viewWorkspace.autosaveEnabled` は `true`（5分間隔、retention 10）です。

Revit UI:
- リボン `RevitMCPServer` タブ → `Workspace` パネル
  - `Autosave Toggle`: `viewWorkspace.autosaveEnabled` をON/OFF（interval/retention は維持）
  - `Reset Defaults`: `viewWorkspace` 設定を初期化（`autoRestoreEnabled=true`, `autosaveEnabled=true`, `autosaveIntervalMinutes=5`, `retention=10`, `includeZoom=true`, `include3dOrientation=true`）

## パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---:|:---:|---|
| enabled | bool | はい |  |
| interval_minutes | int | いいえ | 5 |
| retention | int | いいえ | 10 |
| auto_restore_enabled | bool | いいえ | 変更しない |

注意:
- `retention > 1` の場合、`workspace_{doc_key}.json` に加えて `workspace_{doc_key}_YYYYMMDD_HHMMSS.json` を保存し、古いものを自動削除します。

## 関連
- [save_view_workspace](save_view_workspace.md)
- [restore_view_workspace](restore_view_workspace.md)
- [get_view_workspace_restore_status](get_view_workspace_restore_status.md)
