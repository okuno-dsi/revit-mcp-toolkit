# update_grid_name

- Category: GridOps
- Purpose: Grid(通り芯) 取得/作成/改名/移動/削除

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: update_grid_name

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| name | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_grid_name",
  "params": {
    "name": "..."
  }
}
```

## Related
- get_grids
- create_grids
- move_grid
- delete_grid
- adjust_grid_extents

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "name": {
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
