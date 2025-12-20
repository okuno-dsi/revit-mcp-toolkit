# get_grids

- Category: GridOps
- Purpose: Grid(通り芯) 取得/作成/改名/移動/削除

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_grids
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_grids",
  "params": {}
}
```

## Related
- create_grids
- update_grid_name
- move_grid
- delete_grid
- adjust_grid_extents

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
