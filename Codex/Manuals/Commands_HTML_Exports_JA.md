# HTML 出力コマンド（ダッシュボード）

本書は RevitMCP Add-in の HTML 出力系コマンドのうち、ダッシュボード出力の使い方をまとめたものです。

重要（仕様変更）
- `export_schedules_html` はアドインから削除されました。集計表の書き出しは CSV/Excel の安定版コマンドをご利用ください。
  - 安定版: `export_schedule_to_csv`, `export_schedule_to_excel`

## 共通事項
- 出力文字コードは UTF-8。
- 既存ファイルは上書きします。
- ファイル名には無効文字が含まれる場合「_」に置換します。
- Revit のアクティブ ドキュメントが必要です（開いていない場合はエラー）。
- HTTP 経由で呼び出す際は Content-Type に `application/json; charset=utf-8` を指定してください。

---

## export_dashboard_html
- 目的: プロジェクト情報と主要サマリ（レベル、部屋、カテゴリ/タイプ概況）を単一の HTML に出力します。
- 出力: `index.html`（既定は マイドキュメント\RevitMCP_Dashboard）
- パラメータ:
  - outDir: 出力フォルダの絶対パス（省略時は既定）

例（HTTP シンプルルート）
- PowerShell
  - `Invoke-RestMethod 'http://127.0.0.1:5210/rpc/export_dashboard_html' -Method Post -ContentType 'application/json; charset=utf-8' -Body (@{ outDir = 'C:\Users\okuno\Documents\VS2022\Ver441\Codex\Work\DashboardOut' } | ConvertTo-Json -Compress)`

例（JSON-RPC）
- PowerShell
  - `Invoke-RestMethod 'http://127.0.0.1:5210/rpc' -Method Post -ContentType 'application/json; charset=utf-8' -Body (@{ jsonrpc='2.0'; id='1'; method='export_dashboard_html'; params=@{ outDir='C:\Users\okuno\Documents\VS2022\Ver441\Codex\Work\DashboardOut' } } | ConvertTo-Json -Compress)`

JSONL 一行例
- `{ "jsonrpc":"2.0","id":"1","method":"export_dashboard_html","params":{"outDir":"C:\\Users\\okuno\\Documents\\VS2022\\Ver441\\Codex\\Work\\DashboardOut"} }`

---
## トラブルシュート
- 415 Unsupported Media Type: `-ContentType 'application/json; charset=utf-8'` を付与し、`ConvertTo-Json -Compress` を使用してください。
- No active document.: Revit で対象プロジェクトを開いてから実行してください。
- Create outDir failed: 出力フォルダの権限・パスを確認してください。
- 日本語名について: UTF-8 で保存されます。ファイル名に無効文字がある場合は自動で置換されます。

参考
- 集計表の書き出しは `Manuals/Schedule_Exports_Guide_JA.md` を参照してください（CSV/Excel）。
