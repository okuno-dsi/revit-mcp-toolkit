# agent_bootstrap

- Category: MetaOps
- Purpose: JSON-RPC "agent_bootstrap" handler (Add-in側実体)

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: agent_bootstrap
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "agent_bootstrap",
  "params": {}
}
```

## Result Shape (Summary)
- `server`: basic information about the Revit process (`product`, `process.pid`).
- `project`: legacy project/document info (name, number, filePath, revitVersion, documentGuid, message).
- `environment`: legacy environment info (units, activeViewId, activeViewName).
- `document`: unified document context (recommended for new clients):
  - `ok`, `name`, `number`, `filePath`, `revitVersion`, `documentGuid`
  - `activeViewId`, `activeViewName`
  - `units`: `{ input, internalUnits }`

Existing clients that read `project.*` and `environment.*` continue to work; new clients should prefer `document.*`.

## Related
- search_commands
- start_command_logging
- stop_command_logging

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
