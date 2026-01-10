# サーバーのドキュメント出力（OpenRPC / OpenAPI / Markdown）

サーバーは、アドインから送られてくるマニフェストを元に、API一覧ドキュメントを自動生成できます。

## マニフェスト登録

Revit アドインは起動時に、サーバーへコマンドマニフェストを送信します（best-effort）:
- `POST /manifest/register`

サーバーが把握している内容は次で確認できます:
- `GET /docs/manifest.json`
  - 既定は canonical-only（deprecated alias を除外）です。deprecated も含める場合は `GET /docs/manifest.json?includeDeprecated=1`。

## 生成されるドキュメント

- `GET /docs/openrpc.json` — OpenRPC（JSON‑RPC メソッド一覧）
- `GET /docs/openapi.json` — OpenAPI（仮想 `/rpc/{method}` パス）
- `GET /docs/commands.md` — 人間向け Markdown 一覧
- `GET /debug/capabilities` — 機械可読なコマンド一覧（capabilities）
  - 既定は canonical-only（deprecated alias を除外）です。deprecated も含める場合は `GET /debug/capabilities?includeDeprecated=1`。
  - alias→canonical の関係を一覧で見たい場合は `GET /debug/capabilities?includeDeprecated=1&grouped=1`。

また、サーバーはマニフェストの読み込み/登録時に（best‑effort）JSONL ファイル（1行=1コマンド）も出力します:
- `docs/capabilities.jsonl`

注意:
- ドキュメントは「最後に受信したマニフェスト」（またはディスクキャッシュ）を反映します。
- 空の場合は、Revit を起動してアドインが同じポートのサーバーに接続しているか確認してください。
- capabilities の各フィールドはスキーマ安定です（値が取得できない場合は安全な既定値で補完されます）。
- capabilities の各レコードには `canonical`（deprecated alias の正規名）が含まれるため、エージェント側はヒューリスティック無しで alias→canonical を解決できます。


