# stop_command_logging

- Category: MetaOps
- Purpose: コマンドログの記録停止（ファイルは残す）

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: stop_command_logging
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "stop_command_logging",
  "params": {}
}
```

## Related
- search_commands
- start_command_logging
- agent_bootstrap

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
