# Response Envelope (Step 1)

RevitMCP now adds a **standard set of fields** to every command payload so agents/scripts can reliably:
- detect success/failure (`ok`)
- branch on machine-readable error (`code`)
- show a human message (`msg`)
- diagnose latency (`timings`)

## Where to read it
This repo currently uses a “router wrapper”, so the practical payload is usually:
- `response.result.result`  (most commands)

Some async/utility paths may return payload at:
- `response.result`

## Standard fields (always present in the payload)
- `ok`: `true | false`
- `code`: `"OK"` or an error code string (e.g. `"UNKNOWN_COMMAND"`, `"INVALID_PARAMS"`)
- `msg`: human-readable message
- `warnings`: array of strings (may be empty)
- `timings`: timing info (see below)
- `context`: minimal context (doc title, active view, etc.)
- `nextActions`: array of `{ method, reason }` (may be empty)

## Context token (Step 7)
`context` now includes drift-detection fields:
- `context.contextTokenVersion`: currently `ctx.v1`
- `context.contextRevision`: per-document monotonic counter (updated by document changes, selection/view changes, etc.)
- `context.contextToken`: hash of (doc + view + revision + selection)

If you pass `params.expectedContextToken` to many commands, the add-in will fail fast with `code: PRECONDITION_FAILED` when the token does not match.

## Failure handling gate (Step 10)
For batch-like operations (heuristic: `elementIds` count >= 5), the router may refuse to execute and return:
- `code: "FAILURE_HANDLING_CONFIRMATION_REQUIRED"`

To proceed, re-run the same command and explicitly set `params.failureHandling`:
- off: `{ "failureHandling": { "enabled": false } }`
- rollback (default): `{ "failureHandling": { "enabled": true, "mode": "rollback" } }`
- delete: `{ "failureHandling": { "enabled": true, "mode": "delete" } }`
- resolve: `{ "failureHandling": { "enabled": true, "mode": "resolve" } }`

When `failureHandling` is explicitly provided, the payload may include a `failureHandling` block with whitelist load status and captured failures (`issues`).

Additionally, when a command results in rollback / transaction-not-committed (e.g. `code: "TX_NOT_COMMITTED"`), the router may attach rollback diagnostics (`failureHandling.issues`, `failureHandling.rollbackDetected`, etc.) even if `failureHandling` was not explicitly provided, so warning details are always recorded.

## Timings
- `timings.queueWaitMs`: time waiting in the durable queue (server-derived)
- `timings.revitMs`: time spent executing in Revit (add-in measurement; server fills if missing)
- `timings.totalMs`: end-to-end time since enqueue (server-derived)

Use this to quickly tell whether delays are caused by queueing vs. Revit execution.
