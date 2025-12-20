# delete_space

- Category: Space
- Purpose: Delete Space in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: delete_space
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "delete_space",
  "params": {}
}
```

## Related
- create_space
- get_space_params
- get_spaces
- move_space
- update_space
- get_space_boundary
- get_space_boundary_walls
- get_space_centroid

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
