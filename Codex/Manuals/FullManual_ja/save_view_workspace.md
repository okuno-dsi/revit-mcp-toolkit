# save_view_workspace

- カテゴリ: UI / ビュー
- 目的: 現在の **作業ビュー状態（View Workspace）**（開いているビュー一覧、アクティブビュー、各ビューのズーム、3Dカメラ）を、外部JSONスナップショットとして保存します。スナップショットは MCP Ledger の `doc_key` に紐づきます。

## 概要
スナップショットは次に保存されます:

- `%APPDATA%\\RevitMCP\\ViewWorkspace\\workspace_{doc_key}.json`

`doc_key` は Revit の `DataStorage` に保存される MCP Ledger の `ProjectToken` を利用します。これにより、別プロジェクトへの誤復元を防ぎます。

制限/注意:
- ウィンドウ配置（ドッキング、タブ順、モニタ配置など）は Revit API で復元できません。
- ズーム/カメラは best-effort です（ビュー種別によってはズーム矩形APIが使えません）。

## パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---:|:---:|---|
| doc_key | string | いいえ | 現在の Ledger `ProjectToken` |
| sink | string | いいえ | `file` |
| include_zoom | bool | いいえ | 設定（`viewWorkspace.includeZoom`） |
| include_3d_orientation | bool | いいえ | 設定（`viewWorkspace.include3dOrientation`） |
| retention | int | いいえ | 設定（`viewWorkspace.retention`） |

## 結果（概要）
- `doc_key`: 保存先のキー（Ledgerに紐づく）
- `savedPath`: 保存されたファイルパス
- `openViewCount`: 取得できた開いているビュー数
- `activeViewUniqueId`: 保存時のアクティブビュー
- `warnings`:（任意）注意メッセージ

## 関連
- [restore_view_workspace](restore_view_workspace.md)
- [set_view_workspace_autosave](set_view_workspace_autosave.md)
- [get_view_workspace_restore_status](get_view_workspace_restore_status.md)

