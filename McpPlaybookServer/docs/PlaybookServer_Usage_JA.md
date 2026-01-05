# MCP Playbook Server 利用ガイド（説明書）

バージョン: 1.0  
対象: RevitMCP を安全に記録・再生しながら操作するための外部 HTTP プロキシ（.NET 8）

---

## 1) 目的と概要
- 目的: AI エージェントやスクリプトと RevitMCP の間に挟み、JSON-RPC を記録（teach）し、レシピで安全に再生（replay）します。
- 特徴:
  - Revit アドイン側の変更不要（.NET Framework 4.8 のまま）
  - 別プロセス（.NET 8）として動作
  - 耐久フロー（enqueue/job）と従来フロー（rpc/get_result）の両方をパススルー

---

## 2) 動作環境
- Windows 10/11（x64）
- .NET Runtime 8 以降（開発時: SDK 9.0.305/Runtime 8.0/9.0）
- RevitMCP サーバー（例: http://127.0.0.1:5210）
- Visual Studio 2022（任意、ビルド済みバイナリをそのまま使用可）

---

## 3) 配置とビルド
- ソリューション: `McpPlaybookServer.sln`
- サーバー本体（エントリ）: `src/McpPlaybookServer/Program.cs`
- プロジェクト: `src/McpPlaybookServer/McpPlaybookServer.csproj`
- ビルド/発行済みバイナリ:
  - `src/McpPlaybookServer/bin/Release/net8.0/win-x64/publish/McpPlaybookServer.exe`

ビルド（任意）:
- `dotnet build src/McpPlaybookServer/McpPlaybookServer.csproj -c Release`
- `dotnet publish src/McpPlaybookServer/McpPlaybookServer.csproj -c Release -r win-x64 /p:PublishSingleFile=true`

---

## 4) 起動方法（ポート/転送先）
- 例: RevitMCP が `http://127.0.0.1:5210` でリッスン
- Playbook Server を 5209 で起動

```
McpPlaybookServer.exe --forward http://127.0.0.1:5210 --port 5209
```

---

## 5) 提供エンドポイント（一式）
- Teach（記録）
  - `POST /teach/start?name={optional}` → `{ ok:true, dir }`
  - `POST /teach/stop` → `{ ok:true }`
- 従来フロー（レガシー互換）
  - `POST /rpc` → RevitMCP `/rpc` へ転送（キュー投入/即時応答の形はサーバーに依存）
  - `GET  /get_result` → RevitMCP `/get_result` へ転送
- 耐久フロー（推奨）
  - `POST /enqueue` → RevitMCP `/enqueue` へ転送（`{"jsonrpc":"2.0","id", "method", "params"}`）
  - `GET  /job/{id}` → RevitMCP `/job/{id}` へ転送
  - 動的切替（単一 Playbook で複数 Revit を扱う場合）
    - `POST /t/{port}/enqueue` → `http://127.0.0.1:{port}/enqueue` へ転送
    - `GET  /t/{port}/job/{id}` → `http://127.0.0.1:{port}/job/{id}` へ転送
- Replay（レシピ実行）
  - `POST /replay` → レシピ（`recipe.json`）を読み込み、DryRun/実行を制御
  - 動的切替: `POST /t/{port}/replay` → 指定ポートの `/rpc` へ送信して再生

備考:
- teach モード中は `/rpc` と `/enqueue` の呼び出しが正規化され、`capture.jsonl` に追記されます。

---

## 6) 記録データの保存先
- ルート: `%LOCALAPPDATA%\RevitMCP\Playbooks\`
- セッションフォルダ: `YYYYMMDD_HHMMSS_<session-name>\`
- 主なファイル:
  - `capture.jsonl`（1 RPC 1 行の JSON、時刻/メソッド/パラメータ/結果）
  - `recipe.json`（再生レシピ、変数/期待値を含む）
  - `playbook.md`（メモ）
  - `summary.yaml`（メタ情報）

---

## 7) 典型フロー（すべて Playbook Server 経由）

前提: サーバーは `http://127.0.0.1:5209` で稼働中。

- Ping（耐久フロー）
```
# PowerShell
$base = 'http://127.0.0.1:5209'
$payload = @{ jsonrpc='2.0'; id=1; method='ping_server'; params=@{} } | ConvertTo-Json -Depth 10
Invoke-RestMethod -Uri "$base/enqueue" -Method Post -Body $payload -ContentType 'application/json; charset=utf-8'
# → { ok:true, jobId:"..." }
Invoke-WebRequest -Uri "$base/job/<jobId>" -Method Get -UseBasicParsing
# → state=SUCCEEDED, result_json に JSON-RPC 応答
```

- プロジェクト情報の取得
```
# 1) 動的切替（例: RevitMCP が 5211 で待受）
$base = 'http://127.0.0.1:5209/t/5211'
$payload = '{"jsonrpc":"2.0","id":1,"method":"get_project_info","params":{}}'
Invoke-RestMethod -Uri "$base/enqueue" -Method Post -Body $payload -ContentType 'application/json; charset=utf-8'
# → jobId を受け取り、"$base/job/<jobId>" をポーリング

# 2) 既存の Python スクリプトをそのまま使う場合（ポート = Playbook 側）
python Codex/Manuals/Scripts/send_revit_command_durable.py --port 5209 --command get_project_info --wait-seconds 120 --timeout-sec 120
```

- 現在ビューの要素 ID と壁タイプ名一覧（抜粋）
```
# 1) 現在ビュー ID
python Codex/Manuals/Scripts/send_revit_command_durable.py --port 5209 --command get_current_view --wait-seconds 120 --timeout-sec 120

# 2) ビュー内要素 ID 一覧（idsOnly）
python Codex/Manuals/Scripts/send_revit_command_durable.py --port 5209 --command get_elements_in_view --params '{"viewId": <viewId>, "_shape": {"idsOnly": true, "page": {"limit": 20000}}}' --wait-seconds 120 --timeout-sec 120

# 3) 要素詳細（rich=true）を分割取得 → 壁（categoryId=-2000011）にフィルタし、typeName を抽出
python Codex/Manuals/Scripts/send_revit_command_durable.py --port 5209 --command get_element_info --params '{"elementIds":[...],"rich":true}' --wait-seconds 120 --timeout-sec 120
```

---

## 8) Replay（レシピ実行）
- 乾式（DryRun）で必ず検証し、期待値（`min/max*`）を満たすか確認
- 作成/変更系は `params` に `"confirm": true` を含めること
- 必須変数（`{{var}}`）が未解決のままなら実行拒否

例（HTTP 本文）:
```
POST /replay
{
  "RecipePath": "C:/path/to/recipe.json",
  "DryRun": true,
  "Args": {"target_wall_type":"RC150","level_name":"1FL"}
}
```

---

## 9) セキュリティ/運用の推奨
- `localhost` のみでリッスン（外部公開は最小限）
- teach 中に記録されるログはプロジェクト情報を含む場合あり、アクセス権に注意
- 初回は必ず DryRun、変数/期待値/confirm を厳格に

---

## 10) トラブルシュート
- `POST /enqueue` が 500 の場合:
  - Content-Type を `application/json; charset=utf-8` にする
  - 本サーバーを再起動し、RevitMCP `/enqueue` 単体で通るか確認
- `GET /job/{id}` が 404 の場合:
  - `jobId` の入力ミスまたは期限切れ
- 応答待ちが続く（204/304）:
  - ジョブ実行中。`Retry-After` ヘッダーやバックオフを活用
- ポートが LISTEN していない:
  - `Test-NetConnection localhost -Port 5209` で確認。プロセス未起動やポート競合を確認

---

## 11) 付録: Teach の使い方
```
curl -X POST "http://127.0.0.1:5209/teach/start?name=my-session"
# … 通常の操作を実行（/rpc または /enqueue 経由） …
curl -X POST "http://127.0.0.1:5209/teach/stop"
# 記録は %LOCALAPPDATA%\RevitMCP\Playbooks\my-session\capture.jsonl に保存
```

---

## 12) 既知の仕様
- 本サーバーは JSON を JSON として透過することを優先し、`/enqueue` では JSON ボディを一度バッファに読み込み、明示的に `application/json; charset=utf-8` を付与して RevitMCP に転送します。
- teach 中は `/rpc` および `/enqueue` の呼び出しを正規化して `capture.jsonl` へ追記します（`method`/`params`/`result`）。

---

## 13) ライセンス
- サンプル実装は Apache-2.0（組織ポリシーに従い変更可）
