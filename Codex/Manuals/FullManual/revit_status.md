# revit.status

Server‑only status/telemetry command. Works even when Revit is busy because it does **not** enqueue a Revit job.

## What it returns

- Server identity: `serverPid`, `serverPort`, `startedAtUtc`, `uptimeSec`
- Queue counts: `queue.countsByState`, plus `queuedCount/runningCount/dispatchingCount`
- Current active job (best effort): `activeJob`
- Most recent failure/timeout (best effort): `lastError`
- Stale cleanup (best effort): `staleCleanup`
  - To prevent “ghost RUNNING jobs” after a crash, the server may reclaim extremely old `RUNNING/DISPATCHING` rows as `DEAD` with `error_code: "STALE"`.
  - Threshold can be configured via `REVIT_MCP_STALE_INPROGRESS_SEC` (default: 21600 seconds).

## How to call

Using the durable helper:
```powershell
python .\Manuals\Scripts\send_revit_command_durable.py --port 5210 --command revit.status
```

Via JSON‑RPC endpoint:
```json
{ "jsonrpc":"2.0", "id":1, "method":"revit.status", "params":{} }
```
