# search_commands

- Category: MetaOps
- Purpose: Search Commands in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: search_commands

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| q | unknown | yes |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "search_commands",
  "params": {
    "q": "..."
  }
}
```

## Related
- start_command_logging
- stop_command_logging
- agent_bootstrap

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "q": {
      "type": [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "array",
        "null"
      ]
    }
  },
  "required": [
    "q"
  ]
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
