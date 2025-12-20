# RhinoMCP Operations Runbook

This runbook provides practical steps to start/stop, validate, diagnose, and recover RhinoMCP across its three parts: Server (5200), Plugin IPC (5201), and Revit MCP (5210).

## Start / Stop

Server (5200)
- Start: `pwsh scripts/start_server.ps1 -Url http://127.0.0.1:5200`
- Health: `GET http://127.0.0.1:5200/healthz` → `{ ok: true, name: "RhinoMcpServer" }`
- Stop: kill PID from `server.pid` or `Stop-Process -Name dotnet`

Plugin IPC (5201)
- Starts when Rhino loads the plugin (`RhinoMcpPlugin.rhp`)
- Stop: unloading the plugin or exiting Rhino shuts it down

Revit MCP (5210)
- Start per your environment; ensure `/rpc` accepts `apply_transform_delta` and `get_instance_geometry`

## Validation

1) Baseline
- Server `/healthz` returns ok
- `POST /rpc` with unknown method returns 200 JSON‑RPC error (-32601)

2) Import / Selection / Commit
- `scripts/test_rpc.ps1` performs: health → unknown → import → selection → commit
- Expect import ok on first run; selection shows items when object selected; commit requires Revit MCP up

3) Import by IDs
- Call `rhino_import_by_ids` with actual Revit UniqueIds once Revit MCP is up

## Logs and Diagnostics

- Server log: `%LOCALAPPDATA%\RhinoMCP\logs\RhinoMcpServer.log` (JSON lines per request)
- Plugin log: `%LOCALAPPDATA%\RhinoMCP\logs\RhinoMcpPlugin.log`
- Common issues:
  - Build fails due to file lock → terminate running `dotnet` processes
  - `/rpc` returns HTTP 500 → stale server binary; stop all `dotnet`, rebuild, restart
  - IPC connection refused → ensure plugin is loaded in Rhino (5201 listening), and server points to 5201

## Configuration

- Ports (default): Server 5200 / Plugin IPC 5201 / Revit MCP 5210
- Change locations:
  - Server listen URL → `RhinoMcpServer/Program.cs`
  - Server → Plugin IPC URL → `RhinoMcpServer/Rpc/Rhino/PluginIpcClient.cs`
  - Plugin IPC listen URL → `RhinoMcpPlugin/Core/PluginIpcServer.cs`
- Plugin defaults (server/Revit URLs) → `RhinoMcpPlugin/Plugin.cs`

## Recovery Playbooks

- Rebuild with clean output
  1) Stop all `dotnet` processes
  2) `dotnet clean` + `dotnet build` under `RhinoMcpServer`
  3) Start server script at 5200

- Re‑import after failure
  1) Verify plugin IPC (5201) listening in Rhino
  2) Retry `rhino_import_snapshot` or `rhino_import_by_ids`
  3) If duplicate block name exists, update import logic to reuse/replace definition (see backlog)

## Backlog / Improvements

- Refresh flow from Revit (`rhino_refresh_from_revit` end‑to‑end)
- Reuse/replace existing block definitions on repeated imports
- Full 3D rotation or per‑object axis support
- Harden process lifecycle (auto‑restart, exponential backoff for IPC)
- Structured error catalog with stable codes and messages

## Quick Commands (PowerShell)

```powershell
# Start server
pwsh scripts/start_server.ps1 -Url http://127.0.0.1:5200

# Health
Invoke-RestMethod http://127.0.0.1:5200/healthz

# Unknown method
Invoke-WebRequest -Method POST -Uri http://127.0.0.1:5200/rpc -Body '{"jsonrpc":"2.0","id":1,"method":"unknown_method","params":{}}' -ContentType 'application/json'

# Import by ids
Invoke-WebRequest -Method POST -Uri http://127.0.0.1:5200/rpc -Body '{"jsonrpc":"2.0","id":2,"method":"rhino_import_by_ids","params":{"uniqueIds":["REVIT-UID"],"revitBaseUrl":"http://127.0.0.1:5210"}}' -ContentType 'application/json'
```

