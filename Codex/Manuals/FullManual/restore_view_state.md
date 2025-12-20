# restore_view_state

- Category: ViewOps
- Purpose: Restore View State in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: restore_view_state
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "restore_view_state",
  "params": {}
}
```

## Related
- get_current_view
- get_view_info
- save_view_state
- create_view_plan
- create_section
- create_elevation_view
- compare_view_states
- get_views

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
