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

## Timings
- `timings.queueWaitMs`: time waiting in the durable queue (server-derived)
- `timings.revitMs`: time spent executing in Revit (add-in measurement; server fills if missing)
- `timings.totalMs`: end-to-end time since enqueue (server-derived)

Use this to quickly tell whether delays are caused by queueing vs. Revit execution.
