# RevitMCP.A2AAdapter

Standalone official-A2A-facing adapter for Revit MCP.

This project intentionally does not implement the `LocalA2A` file transport. It exposes an A2A-style HTTP/JSON-RPC surface and bridges deterministic requests to the existing local `RevitMCPServer` queue.

## Endpoints

- `GET /health`
- `GET /.well-known/agent-card.json`
- `GET /a2a/agent-card`
- `POST /a2a/rpc`

Supported JSON-RPC methods:

- `SendMessage`
- `GetTask`
- `ListTasks`
- `CancelTask`
- `GetExtendedAgentCard`

## Request Mapping

`SendMessage` accepts a deterministic Revit MCP command in either `params.metadata` or a data part:

```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "method": "SendMessage",
  "params": {
    "message": {
      "role": "user",
      "parts": [
        {
          "kind": "data",
          "data": {
            "revitMethod": "get_context",
            "revitParams": {}
          }
        }
      ]
    },
    "configuration": {
      "returnImmediately": true
    }
  }
}
```

The adapter forwards the request to `RevitMCPServer` as a JSON-RPC call:

```text
POST {RevitMcpServerUrl}/rpc
```

The default target is `http://127.0.0.1:5210`.

## Configuration

Settings can be supplied in `appsettings.json`, environment variables, or command-line arguments:

- `A2AAdapter:Port` / `REVIT_MCP_A2A_PORT` / `--port=5220`
- `A2AAdapter:BindHost` / `REVIT_MCP_A2A_BIND_HOST` / `--bind-host=127.0.0.1`
- `A2AAdapter:RevitMcpServerUrl` / `REVIT_MCP_A2A_TARGET_URL` / `--target=http://127.0.0.1:5210`
- `A2AAdapter:ProtocolVersion` / `REVIT_MCP_A2A_PROTOCOL_VERSION`

The default bind host is loopback. Keep it loopback unless the deployment has a separate authentication and network policy.
