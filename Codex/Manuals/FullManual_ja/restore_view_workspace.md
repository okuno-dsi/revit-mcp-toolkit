# restore_view_workspace

- カテゴリ: UI / ビュー
- 目的: 保存済みの **作業ビュー状態（View Workspace）**（開いているビュー、アクティブビュー、ズーム/3Dカメラ）を復元します。

## 概要
スナップショットは外部ファイルから読み込みます（既定）:

- `%APPDATA%\\RevitMCP\\ViewWorkspace\\workspace_{doc_key}.json`

安全性:
- 現在の `doc_key` は MCP Ledger（`DataStorage` の `ProjectToken`）から解決します。
- スナップショットの `doc_key` と現在の `doc_key` が一致する場合のみ復元します。
- `doc_path_hint` と現在のファイルパスが違う場合は **警告** を返します（`doc_key` が一致していれば復元は継続）。

実行モデル:
- 復元は **Idling で段階実行** されます（ビューをアクティブ化→ズーム/カメラ適用→次のビュー）。
- 進捗確認は `get_view_workspace_restore_status` を使ってください。
 - ビューが存在しない（削除/変更など）場合はスキップし、`missingViews` に計上されます（必要なら `warnings` を確認）。

自動動作:
- プロジェクトを開いたときの自動restoreは **デフォルトON** です（`%LOCALAPPDATA%\\RevitMCP\\settings.json` の `viewWorkspace.autoRestoreEnabled` でOFF可）。

## パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---:|:---:|---|
| doc_key | string | いいえ | 現在の Ledger `ProjectToken` |
| source | string | いいえ | `file`（`auto` も受理し file扱い） |
| include_zoom | bool | いいえ | 設定（`viewWorkspace.includeZoom`） |
| include_3d_orientation | bool | いいえ | 設定（`viewWorkspace.include3dOrientation`） |
| activate_saved_active_view | bool | いいえ | true |

## 結果（概要）
- `msg`: 受付メッセージ（復元はバックグラウンド継続）
- `restoreSessionId`: `get_view_workspace_restore_status` で参照するID
- `openViews`: 復元対象のビュー数
- `warnings`:（任意）doc_path_hint 不一致など

## 関連
- [save_view_workspace](save_view_workspace.md)
- [set_view_workspace_autosave](set_view_workspace_autosave.md)
- [get_view_workspace_restore_status](get_view_workspace_restore_status.md)
