# Codex～Revit MCP 接続コマンド送受信手順

## 目的
Codex（この開発用CLI）から Revit MCP アドインへ JSON-RPC コマンドを送受信する最小・確実な手順をまとめます。まずは疎通確認用 `ping_server` を用いて、アドイン到達性と応答を検証します。

## 前提条件
- Revit が起動し、MCP アドインが稼働中であること。
- Revit の MCP ポート番号（例: `5210`）が分かっていること。
- Windows PowerShell（5+ または PowerShell 7+）が利用可能。
- Python クライアントを使う場合は Python 3.x が利用可能。

## 推奨エンドポイント
- `http://127.0.0.1:<PORT>` を推奨します（`localhost` でも可）。
- 例: `http://127.0.0.1:5210`

## 最小疎通確認（ping_server）
### PowerShell（手動2ステップ）
1) enqueue
```
Invoke-WebRequest -Method Post \
  -Uri "http://127.0.0.1:5210/enqueue" \
  -Headers @{ 'Content-Type'='application/json; charset=utf-8' } \
  -Body '{"jsonrpc":"2.0","method":"ping_server","params":{},"id":1}' \
  -SkipHttpErrorCheck
```
- 成功例: `{"ok":true,"commandId":"<GUID>"}` が返る

2) get_result（ポーリング）
```
$cid = '<上で返ったcommandId>'
Invoke-WebRequest -Method Get \
  -Uri "http://127.0.0.1:5210/get_result?commandId=$cid" \
  -Headers @{ 'Accept'='application/json; charset=utf-8' } \
  -SkipHttpErrorCheck
```
- 成功例（抜粋）:
```
{"jsonrpc":"2.0","result":{"ok":true,"msg":"MCP Server round-trip OK (Revit Add-in reachable)" ...},"id":1}
```

### PowerShell（ワンライナー例: 簡易ポーリング）
```
$base = "http://127.0.0.1:5210";
$h = @{ 'Content-Type'='application/json; charset=utf-8'; 'Accept'='application/json; charset=utf-8' };
$b = '{"jsonrpc":"2.0","method":"ping_server","params":{},"id":1}';
$r = Invoke-WebRequest -Method Post -Uri "$base/enqueue" -Headers $h -Body $b -SkipHttpErrorCheck;
$cid = ($r.Content | ConvertFrom-Json).commandId;
for ($i=0; $i -lt 30; $i++){ Start-Sleep -Milliseconds 500; $g = Invoke-WebRequest -Method Get -Uri "$base/get_result?commandId=$cid" -Headers $h -SkipHttpErrorCheck; if ($g.StatusCode -eq 200 -and $g.Content){ $g.Content; break } }
```

### Python クライアント（推奨）
リポジトリ同梱の汎用クライアントを利用します。
```
chcp 65001 > NUL & python Scripts/Reference/send_revit_command_durable.py --port 5210 --command ping_server
```
- 正常時は `result.ok == true` の JSON を出力します。

## よくあるエラーと対処
- 500 Internal Server Error（enqueue 時）
  - 送信 JSON に `"params": {}` を必ず含めてください（空でも必須のサーバー実装があります）。
  - Revit がビジーの場合は `?force=1` を付与し再送（例: `.../enqueue?force=1`）。
  - MCP が起動していない／ポート不一致の可能性。リボンUIやログのポート表示を再確認。
- タイムアウト（get_result ポーリング）
  - Revit 側にダイアログやモーダルが出ていないか確認・解除。
  - アクティブなプロジェクトが開かれているか確認。
  - `ping_server` の応答に `issues` が含まれる場合は内容を確認。
- 409 Conflict（ジョブ競合）
  - 既存ジョブが走っている可能性。`/enqueue?force=1` を検討。

## 実運用パターン（要点）
- 送信: `POST /enqueue` に JSON-RPC ボディを送る（`jsonrpc`/`method`/`params`/`id`）。
- 受信: `GET /get_result?commandId=...` を 200 応答まで短周期ポーリング（202/204 は未完了）。
- 成功判定: `result.ok == true` または `{ ok: true, ... }` 形式。

## 参考資料
- コマンド一覧（抜粋に `ping_server` 例あり）
  - `コマンドハンドラ一覧/Most Important コマンドハンドラ一覧（カテゴリ別）20250901_AI向け.txt`
- 汎用クライアント
  - `Scripts/Reference/send_revit_command_durable.py`

## 再利用（次回以降のCodex作業方針）
- Revit MCP への接続・疎通確認やコマンド送信が必要になった場合、本手順書（このファイル）を参照して実行します。
- まず `ping_server` で到達性を確認し、次に目的コマンドを `Scripts/Reference/send_revit_command_durable.py` もしくは PowerShell 例に沿って送信します。




