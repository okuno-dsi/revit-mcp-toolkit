# dryRun と confirmToken（高リスク操作の安全策）

`delete_*` / `remove_*` / `reset_*` / `clear_*` / `purge_*` のような **破壊的（高リスク）** コマンドに対して、以下の安全策を提供します。

## 1) `dryRun: true`（ロールバック実行）

- 高リスクコマンドに `dryRun: true` を付けると、外側の `TransactionGroup` 内で実行してから **ロールバック** します。
- コマンド実行後、モデルへの変更は残りません。

レスポンスにはエージェント向けに次が付与されます：
- `dryRunRequested: true`（要求したことの印）
- `dryRun: true` と `dryRunApplied: "transactionGroup.rollback"`（ロールバックが適用された場合）

## 2) confirmToken ハンドシェイク（任意）

高リスクコマンドを `dryRun: true` で成功させると、以下が返ります：
- `confirmToken`（1回限り・短時間で失効）
- `confirmTokenExpiresAtUtc`
- `confirmTokenTtlSec`
- `confirmTokenMethod`

本実行したい場合は、`dryRun` を外し、同じパラメータ（`dryRun` と token 系を除く）で `confirmToken` を渡して再実行します。

さらに `requireConfirmToken: true` を付けると、token が無い限り実行されません（安全側）。

### 例（壁の削除）

dryRun:
```json
{ "jsonrpc":"2.0", "id":1, "method":"delete_walls", "params":{ "elementIds":[123,456], "dryRun":true } }
```

本実行（token必須）:
```json
{ "jsonrpc":"2.0", "id":2, "method":"delete_walls", "params":{ "elementIds":[123,456], "requireConfirmToken":true, "confirmToken":"ct-..." } }
```

token が無い／不正／失効の場合：
- `code: "CONFIRMATION_REQUIRED"` または `code: "CONFIRMATION_INVALID"`


