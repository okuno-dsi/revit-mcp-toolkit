# Durable vs. Legacy Request Flow (Revit MCP)

This document compares the “durable” job-based sender and the traditional (legacy) FIFO sender used to communicate with the Revit MCP add-in.

- Durable sender: `Manuals/Scripts/send_revit_command_durable.py`
- Legacy sender: (deprecated here) — use durable wrapper `..\..\NVIDIA-Nemotron-v3\tool\send_revit_command.py` which implements durable flow
- Optional resilient wrapper: `tools/mcp_safe.py`

## Overview
- Legacy flow polls a global queue via `GET /get_result` after `POST /enqueue`.
- Durable flow receives a per-job identifier from `POST /enqueue` and polls `GET /job/{jobId}`, enabling stronger correlation and better scalability.

## Transport and Endpoints
- Common
  - Enqueue: `POST /enqueue` with a JSON-RPC body `{ jsonrpc, method, params, id }`.
  - Include `params: {}` even when empty (server requires it).
  - Optional override for contention: `/enqueue?force=1`.
- Legacy
  - Typical enqueue response: `{ ok:true, commandId:"<GUID>" }` (or an immediate JSON-RPC result).
  - Polling endpoint: `GET /get_result?commandId=<GUID>` (HTTP 202/204 while pending; 200 when complete).
- Durable
  - Typical enqueue response: `{ ok:true, jobId:"<ID>" }` (or an immediate success if very fast).
  - Polling endpoint: `GET /job/{jobId}`.
  - Supports conditional requests via `ETag` and `If-None-Match`, and server `Retry-After` hints to reduce load.
  - Optional server-side timeout per job: `/enqueue?timeout=<seconds>`.

## Polling Behavior
- Both clients implement adaptive backoff to balance responsiveness and load.
- Legacy: polls `GET /get_result` only; no conditional caching.
- Durable: polls `GET /job/{jobId}`; honors `ETag` and `Retry-After`, handles `304 Not Modified`, `202/204` pending states, then returns the final result.

## Result Correlation and Shape
- Legacy
  - May rely on a `commandId` from enqueue and/or inspect `method/id` inside the returned JSON-RPC envelope to avoid consuming stale results from the global queue.
  - Returns either a JSON-RPC envelope `{ jsonrpc, id, result }` or an `{ ok:true, ... }` object.
- Durable
  - Correlates by `jobId` (no mix-ups across concurrent clients/jobs).
  - On `SUCCEEDED`, returns the decoded `result_json` when present, or `{ ok:true }`.
  - On `FAILED`, `TIMEOUT`, or `DEAD`, returns a structured error with the server-provided message.

## Error Handling and Resilience
- Common
  - Structured errors include the phase (`enqueue`/`get_result`), optional `httpStatus`, and any JSON-RPC `error` payload.
  - HTTP 409 Conflicts (job in progress) are surfaced; callers may retry or use `?force=1`.
- Durable advantages
  - Strong, per-job correlation avoids result mix-ups in high concurrency.
  - Conditional polling reduces network chatter and server load on long-running jobs.
  - Optional per-job timeout helps bound server-side execution.
- Legacy considerations
  - Global queue polling can be noisier under load.
  - Client-side correlation using `method/id` is best-effort and may be less robust when many commands are in flight.

## Performance and Scalability
- Legacy
  - Simple FIFO polling; adequate for low concurrency and short tasks.
- Durable
  - Better suited for multi-instance setups and long-running commands.
  - Fewer round-trips thanks to `ETag`/`Retry-After`, and reduced risk of queue contention.

## CLI Usage Examples
- Durable (recommended when `/job/{id}` is available)
  - Ping
    ```bash
    python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command ping_server --wait-seconds 120 --timeout-sec 120
    ```
  - Get project info (save result)
    ```bash
    python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command get_project_info --wait-seconds 120 --timeout-sec 120 --output-file Work/<ProjectName>_<Port>/Logs/get_project_info_5210.json
    ```
- Legacy (fallback/compatibility)
  - Ping
    ```bash
    python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command ping_server --wait-seconds 120
    ```
  - Get project info (save result)
    ```bash
    python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command get_project_info --wait-seconds 120 --output-file Work/<ProjectName>_<Port>/Logs/get_project_info_5210.legacy.json
    ```

## Operational Notes
- Keep `params: {}` in requests even when no parameters are required.
- Use `--force` or `/enqueue?force=1` if a conflicting job is already running.
- Prefer durable sender by default when the server exposes `/job/{jobId}`; otherwise, fall back to the legacy sender.
- For retries/backoff on busy/timeout conditions, you can also use the resilient wrapper `tools/mcp_safe.py`.

## Observation (This Workspace)
- On 2025-10-14, port `5210` timed out with the legacy sender for `ping_server`, while the durable sender succeeded quickly. Ports `5211` and `5212` also responded successfully with the durable sender.



