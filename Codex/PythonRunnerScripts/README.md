# Python Runner Scripts

このフォルダは **ユーザー向けの汎用スクリプト置き場** です。

- **ここに置いたスクリプトが Python Runner のライブラリ対象** になります。
- **Projects/<ProjectName>_<docKey>/python_script** は「プロジェクト固有のスクリプト」用です。
- **Scripts/Reference** は AI 参照用（テンプレ・検証用）であり、Python Runner 用の保管場所ではありません。
- **PowerShell スクリプト（.ps1）は Python Runner では実行できないため、`Tools/PowerShellScripts/` に集約**します。

運用の目安:
- 汎用・再利用したいスクリプト → ここ
- プロジェクト専用・一時的 → Projects/<ProjectName>_<docKey>/python_script
- AI が参照するテンプレ/検証用 → Scripts/Reference

追加サンプル:
- `column_grid_coreline_workflow_sample.py`
  - 柱芯線図向け（通り芯ビュー作成、柱別トリミング、通り芯長さ調整、通り芯記号非表示、シート重ね配置）
- `snapshot_view_elements_to_sqlite_runner.py`
  - 現在ビュー、または指定ビューの要素スナップショットを SQLite に追記保存します。
  - 既存の `snapshot_view_elements` / `get_elements_in_view` を使うため、比較・監査・検索用の補助DBとして使えます。




