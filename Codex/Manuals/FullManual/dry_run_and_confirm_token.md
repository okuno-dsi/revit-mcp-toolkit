# dryRun and confirmToken (High‑risk safety)

Some commands are destructive (high‑risk), e.g. `delete_*`, `remove_*`, `reset_*`, `clear_*`, `purge_*`.

This add-in supports two safety features for **high‑risk** commands:

## 1) `dryRun: true` (Rollback execution)

- If you send `dryRun: true` to a high‑risk command, the command is executed inside an outer `TransactionGroup` and then **rolled back**.
- No model changes remain after the command returns.

The result is annotated for agents:
- `dryRunRequested: true` when you requested it
- `dryRun: true` and `dryRunApplied: "transactionGroup.rollback"` when rollback was applied

## 2) `confirmToken` handshake (optional)

When a high‑risk command succeeds in `dryRun: true`, the result includes:
- `confirmToken` (one‑time, short‑lived)
- `confirmTokenExpiresAtUtc`
- `confirmTokenTtlSec`
- `confirmTokenMethod`

To execute for real, call the same command again with the **same parameters** (except `dryRun`) and pass `confirmToken`.

If you also pass `requireConfirmToken: true`, the command will refuse to run without a valid token.

### Example (delete walls)

Dry‑run:
```json
{ "jsonrpc":"2.0", "id":1, "method":"delete_walls", "params":{ "elementIds":[123,456], "dryRun":true } }
```

Execute (requires token):
```json
{ "jsonrpc":"2.0", "id":2, "method":"delete_walls", "params":{ "elementIds":[123,456], "requireConfirmToken":true, "confirmToken":"ct-..." } }
```

If the token is missing/invalid/expired:
- `code: "CONFIRMATION_REQUIRED"` or `code: "CONFIRMATION_INVALID"`

