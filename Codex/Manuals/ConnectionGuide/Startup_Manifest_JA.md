# 起動時のコマンド走査とマニフェスト登録（自動）

概要
- Add-in は Revit 起動直後に、アセンブリ内のコマンド実装（`CommandName` と `Execute(...)` を持つ型）を反射で走査し、マニフェストとしてローカルHTTPサーバに登録します。
- 実装箇所: `Manifest/ManifestExporter.cs`（走査/組み立て + `/manifest/register` への POST）
- 呼び出し元: `App.cs` の `OnStartup(...)` から非同期で最大5回リトライ（ベストエフォート）。

挙動のポイント
- 走査は「起動直後に一度だけ」。Add-in のロード時に最新のコマンド集合をサーバ側に知らせる目的です。
- マニフェスト登録が失敗しても、Add-in 内のルータは独立しており、`list_commands` で現在有効なコマンド名一覧をいつでも取得できます（推奨）。

サーバー側の確認（推奨）
- サーバーが受信したマニフェストは `GET /docs/manifest.json` で確認できます（最後に受信した内容を表示）。
  - 既定は canonical-only（deprecated alias を除外）です。deprecated も含める場合は `GET /docs/manifest.json?includeDeprecated=1`。
- 機械可読なコマンド棚卸しは `GET /debug/capabilities` が便利です（ベストエフォート）。
  - 既定は canonical-only（deprecated alias を除外）です。deprecated も含める場合は `GET /debug/capabilities?includeDeprecated=1`。
  - alias→canonical の関係を一覧で見たい場合は `GET /debug/capabilities?includeDeprecated=1&grouped=1`。
  - 同等の内容は `docs/capabilities.jsonl`（1行=1コマンド）としても出力されます（マニフェスト読み込み/登録時、best-effort）。

コマンド一覧の取得
- 簡易（名前のみ）:
```
python Scripts/Reference/send_revit_command_durable.py --port <PORT> --command list_commands --params '{"namesOnly":true}' --output-file Projects/<ProjectName>_<Port>/Logs/list_commands_names.json
```
  - 出力: `result.result.commands` にメソッド名の配列

- 検索（部分一致/カテゴリ指定など）:
```
python Scripts/Reference/send_revit_command_durable.py --port <PORT> --command search_commands --params '{"q":"view list","top":30}'
```

トラブルシューティング
- `/manifest/register` が見つからない/失敗する場合でも、`list_commands` が返す一覧が真実です（Add-in 側の実体）。
- 期待するコマンドが `list_commands` に出てこない場合は、`RevitMcpWorker` のハンドラ登録（`new XxxCommand()`）を確認してください。




