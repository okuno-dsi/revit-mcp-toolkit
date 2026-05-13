# Tools (utilities)

この `Tools/` は **アドイン／サーバー本体のビルドや実行には必須ではない**、補助スクリプト置き場です。  
ただしマニュアル内で参照されているものがあり、運用上は残すことを推奨します。

## 主なスクリプト

- `mcp_safe.py`
  - Revit MCP 等の MCP 呼び出しを「リトライ／バックオフ／タイムアウト耐性」を付けて実行するためのラッパー。
  - 参考: `Manuals/Durable_vs_Legacy_Request_Flow.md`
- `codex_manuals_sqlite.py`
  - `Codex/Manuals` 配下の `.md` / `.txt` を SQLite に索引化します。
  - installer 構成では `Documents/Revit_MCP/Docs/Manuals` を自動検出し、`Documents/Revit_MCP/SQLite/Manuals/codex_manuals.sqlite` を既定保存先として使います。
  - `--query` を付けると、作成済み SQLite を全文検索できます。
- `query_revit_snapshot_sqlite.py`
  - `snapshot_view_elements_to_sqlite_runner.py` が保存した SQLite を検索します。
  - スナップショット一覧、カテゴリ名/ファミリ名/タイプ名での絞り込みに対応します。
- `query_building_law_sqlite.py`
  - installer に同梱した `SQLite/Legal/BuildingCode/` の read-only DB を検索します。
  - `xml_law_index.sqlite`、`gov_document_text_index.sqlite`、`gov_document_pdf_index.sqlite` を横断できます。
  - `--list-laws` で収録法令一覧、`--query` で本文検索、`--include-references` で条文参照も表示できます。
- `Tools/PowerShellScripts/cleanup_old_artifacts.ps1`
  - `Projects/` や `%LOCALAPPDATA%/RevitMCP` 配下のキャッシュ／ログ等を、更新日が古いものから削除する補助（既定: 7日）。
  - `-Execute` を付けないと DRY RUN です。
- スナップショット／復元・差分比較系（作業支援）
  - `save_snapshot_bundle.py`
  - `compare_with_snapshot.py`
  - `reconstruct_from_snapshot.py`
  - `delete_*_snapshot.py`
  - `fix_scaled_walls_from_snapshot.py` / `export_scaled_wall_mappings.py` / `scale_plan_by_ref_wall.py`

## AutoCAD（DWG/DXF）補助

AutoCAD Core Console 等を使う補助スクリプトは `Tools/AutoCad/` に配置しています。

- 例: `Tools/AutoCad/Run_MergeByDXF.ps1`
- `Tools/AutoCad/rename_layers_by_param_group.py`
  - `export_dwg_by_param_groups` の出力DWGに対し、レイヤを `CAT_{category}__PARAM_{paramName}__VAL_{paramValue}` へ統合。
- `Tools/AutoCad/merge_dwgs_by_map_com.py`
  - AutoCAD COMでDWGをマージし、任意のレイヤ名マップ（pattern,target）を適用。

## メモ

- PowerShell スクリプトは `Tools/PowerShellScripts/` に集約しています。
- `Tools/__pycache__/` は Python 実行時に自動生成されるため、削除して問題ありません。

