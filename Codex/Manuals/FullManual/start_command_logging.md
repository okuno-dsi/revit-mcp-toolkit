# start_command_logging

- Category: MetaOps
- Purpose: コマンドログの記録開始（出力先と任意のprefixを設定）

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: start_command_logging

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| dir | string | no/depends |  |
| prefix | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "start_command_logging",
  "params": {
    "dir": "...",
    "prefix": "..."
  }
}
```

## Related
- search_commands
- stop_command_logging
- agent_bootstrap

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "dir": {
      "type": "string"
    },
    "prefix": {
      "type": "string"
    }
  }
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
