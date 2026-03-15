# Revit MCP Connection Quickstart (Port 5210)

## Preferred check: Model Context Protocol over HTTP

Use the standard MCP endpoint first. The Revit automation server now exposes `/mcp` for `initialize`, `tools/list`, and `tools/call`.

```powershell
$base = 'http://127.0.0.1:5210/mcp'
$protocol = '2025-11-25'

$initBody = @{
  jsonrpc = '2.0'
  id = 1
  method = 'initialize'
  params = @{
    protocolVersion = $protocol
    capabilities = @{}
    clientInfo = @{ name = 'manual-check'; version = '1.0' }
  }
} | ConvertTo-Json -Depth 10 -Compress

$init = Invoke-WebRequest -Uri $base -Method POST -ContentType 'application/json' -Headers @{
  Accept = 'application/json, text/event-stream'
  Origin = 'http://localhost'
  'MCP-Protocol-Version' = $protocol
} -Body $initBody

$sessionId = $init.Headers['MCP-Session-Id']

Invoke-WebRequest -Uri $base -Method POST -ContentType 'application/json' -Headers @{
  Origin = 'http://localhost'
  'MCP-Protocol-Version' = $protocol
  'MCP-Session-Id' = $sessionId
} -Body '{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}' | Out-Null

Invoke-RestMethod -Uri $base -Method POST -ContentType 'application/json' -Headers @{
  Origin = 'http://localhost'
  'MCP-Protocol-Version' = $protocol
  'MCP-Session-Id' = $sessionId
} -Body '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'

Invoke-RestMethod -Uri $base -Method POST -ContentType 'application/json' -Headers @{
  Origin = 'http://localhost'
  'MCP-Protocol-Version' = $protocol
  'MCP-Session-Id' = $sessionId
} -Body '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_open_documents","arguments":{}}}'
```

Success criteria:
- `initialize` returns `protocolVersion`, `capabilities.tools`, and `serverInfo`
- `protocolVersion` is negotiated to a supported server value; unsupported future versions are not echoed back
- `tools/list` returns one or more tools
- `tools/call` for `get_open_documents` returns document information without transport errors
- `notifications/initialized` returns HTTP `202 Accepted` with no JSON-RPC body

Optional compatibility checks:
- `resources/list`:
```powershell
Invoke-RestMethod -Uri $base -Method POST -ContentType 'application/json' -Headers @{
  Origin = 'http://localhost'
  'MCP-Protocol-Version' = $protocol
  'MCP-Session-Id' = $sessionId
} -Body '{"jsonrpc":"2.0","id":4,"method":"resources/list","params":{}}'
```
- `prompts/list`:
```powershell
Invoke-RestMethod -Uri $base -Method POST -ContentType 'application/json' -Headers @{
  Origin = 'http://localhost'
  'MCP-Protocol-Version' = $protocol
  'MCP-Session-Id' = $sessionId
} -Body '{"jsonrpc":"2.0","id":5,"method":"prompts/list","params":{}}'
```

## Legacy check: durable RPC

The legacy `/rpc` and `/job` endpoints remain available for existing scripts.

- Verify port: `Test-NetConnection localhost -Port 5210` => `TcpTestSucceeded True`
- Bootstrap: `python Scripts/Reference/send_revit_command_durable.py --port 5210 --command agent_bootstrap --output-file Logs/agent_bootstrap.json`
- List elements in active view (ids):
  `python Scripts/Reference/send_revit_command_durable.py --port 5210 --command get_elements_in_view --params "{\"viewId\": <activeViewId>, \"_shape\":{\"idsOnly\":true,\"page\":{\"limit\":200}}}" --output-file Logs/elements_in_view.json`

## Notes

- Product name: `Revit MCP`
- Protocol name: `Model Context Protocol`
- Prefer `/mcp` for new clients
- Keep `/rpc` and `/job/{id}` only for backward compatibility
- `/mcp` now validates `Origin`; use `http://localhost` or `http://127.0.0.1`
- `/sse`, `/messages`, `/swagger` are not provided in the current server profile
- Use `/docs/openapi.json` or `/docs/openrpc.json` for machine-readable API docs
- Default units remain `Length=mm`, `Angle=deg`; the server converts internally
- Important: never send `viewId: 0` or `elementId: 0`
- Safe write execution for legacy scripts still prefers the `*_safe.ps1` wrappers
