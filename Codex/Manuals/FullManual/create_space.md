# create_space

- Category: Space
- Purpose: create_space（InputPointReader対応＋高さ設定＋ComputeVolumes ON）

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_space

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| heightMm | number | no/depends |  |
| name | string | no/depends |  |
| number | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_space",
  "params": {
    "heightMm": 0.0,
    "name": "...",
    "number": "..."
  }
}
```

## Related
- delete_space
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
  "properties": {
    "name": {
      "type": "string"
    },
    "number": {
      "type": "string"
    },
    "heightMm": {
      "type": "number"
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
