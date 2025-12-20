# ping_server

- Category: Rpc
- Purpose: JSON-RPC "ping_server" – サーバー/アドインの往復疎通を確認

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: ping_server
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "ping_server",
  "params": {}
}
```

## Related
- get_open_documents

### Params Schema
```json
{
  "type": "object",
  "properties": {}
}
```

### Result Schema
```json
{
  "type": "object",
  "properties": {},
  "additionalProperties": true
}
```
