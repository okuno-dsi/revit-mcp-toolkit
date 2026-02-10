# 応答（結果）エンベロープ（Step 1）

RevitMCP はすべてのコマンド応答に **共通フィールド** を付与し、AI/スクリプトが確実に:
- 成功/失敗判定（`ok`）
- 機械判定できるエラーコード（`code`）
- 人間向けメッセージ（`msg`）
- 遅延の原因切り分け（`timings`）
を行えるようにしました。

## どこを見ればよいか
現状は「ルータラッパ」があるため、実際のペイロードは基本的に:
- `response.result.result`（多くのコマンド）

非同期/一部の経路では:
- `response.result`
になる場合があります。

## 共通フィールド（ペイロードに必ず存在）
- `ok`: `true | false`
- `code`: `"OK"` またはエラーコード（例: `"UNKNOWN_COMMAND"`, `"INVALID_PARAMS"`）
- `msg`: 人間向けメッセージ
- `warnings`: 文字列配列（空の場合あり）
- `timings`: 実行時間情報（下記）
- `context`: 最低限のコンテキスト（ドキュメント名、アクティブビュー等）
- `nextActions`: `{ method, reason }` の配列（空の場合あり）

## コンテキストトークン（Step 7）
`context` には状態ドリフト検知のためのフィールドが追加されます:
- `context.contextTokenVersion`: 現在は `ctx.v1`
- `context.contextRevision`: ドキュメント単位の単調増加カウンタ（文書変更、選択/ビュー変更などで更新）
- `context.contextToken`: (doc + view + revision + selection) のハッシュ

多くのコマンドに `params.expectedContextToken` を渡すと、トークン不一致時に `code: PRECONDITION_FAILED` で早期に失敗します。

## 安定ドキュメントキー（docKey）
並行実行や作業フォルダ命名の安定化のため、以下を追加しました:
- `context.docKey`: 安定ドキュメントキー
- `context.docKeySource`: `ledger` または `fallback`
- `context.docKeyStable`: `true | false`（安定ソースから生成されたか）

互換性のため:
- `context.docGuid` は引き続き付与され、`context.docKey` と同一です。

## Idempotency キー（任意）
`params.idempotencyKey`（または `params.idemKey`）を渡すと、同一 `(method + docKey + idempotencyKey)` の結果を短時間キャッシュし、
**重複実行を抑止**することがあります。応答には以下が追加されます:
- `context.idempotencyKey`
- `context.idempotencyCache`: `{ hit: true|false, ttlMs, maxEntries }`

## 安定順序（配列の決定論的並び）
`elements` / `items` / `hosts` などの配列は、`elementId` / `id` / `hostElementId` を優先キーとして **安定ソート**されます。
同一コマンドの繰り返し実行での差分ノイズを減らす目的です。

## 失敗処理（failureHandling）確認ゲート（Step 10）
バッチ的な操作（目安: `elementIds` が 5 件以上）の場合、ルータが実行前に確認を要求し、次を返すことがあります:
- `code: "FAILURE_HANDLING_CONFIRMATION_REQUIRED"`

続行するには、同じコマンドを再実行し、`params.failureHandling` を明示指定してください:
- オフ: `{ "failureHandling": { "enabled": false } }`
- ロールバック（既定）: `{ "failureHandling": { "enabled": true, "mode": "rollback" } }`
- 削除（whitelist 警告のみ）: `{ "failureHandling": { "enabled": true, "mode": "delete" } }`
- 解決（whitelist のみ）: `{ "failureHandling": { "enabled": true, "mode": "resolve" } }`

`failureHandling` を明示した場合、応答ペイロードに `failureHandling` ブロック（whitelist 読み込み状態・捕捉した失敗 `issues` など）が付与されることがあります。

また、**ロールバック／トランザクション未コミット**（例: `code: "TX_NOT_COMMITTED"`）が発生した場合は、`failureHandling` を明示していなくても、警告詳細を必ず記録するために `failureHandling` の診断情報（`issues`, `rollbackDetected` など）が付与されることがあります。

## timings
- `timings.queueWaitMs`: キュー待ち時間（サーバー算出）
- `timings.revitMs`: Revit 内の実行時間（アドイン計測。未設定時はサーバーが補完）
- `timings.totalMs`: enqueue からの総時間（サーバー算出）

これにより「待ち（キュー）」なのか「Revit 内処理」なのかを切り分けできます。
