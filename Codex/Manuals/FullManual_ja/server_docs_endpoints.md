# サーバーのドキュメント出力（OpenRPC / OpenAPI / Markdown）

サーバーは、アドインから送られてくるマニフェストを元に、API一覧ドキュメントを自動生成できます。

## マニフェスト登録

Revit アドインは起動時に、サーバーへコマンドマニフェストを送信します（best-effort）:
- `POST /manifest/register`

サーバーが把握している内容は次で確認できます:
- `GET /docs/manifest.json`

## 生成されるドキュメント

- `GET /docs/openrpc.json` — OpenRPC（JSON‑RPC メソッド一覧）
- `GET /docs/openapi.json` — OpenAPI（仮想 `/rpc/{method}` パス）
- `GET /docs/commands.md` — 人間向け Markdown 一覧

注意:
- ドキュメントは「最後に受信したマニフェスト」（またはディスクキャッシュ）を反映します。
- 空の場合は、Revit を起動してアドインが同じポートのサーバーに接続しているか確認してください。


