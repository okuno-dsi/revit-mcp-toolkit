# merge_areas

- Category: Area
- Purpose: Merge Areas in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: merge_areas

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| refreshView | bool | no/depends | false |
| toleranceMm | number | no/depends | 2.0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "merge_areas",
  "params": {
    "refreshView": false,
    "toleranceMm": 0.0
  }
}
```

## Related
- get_area_boundary
- create_area
- get_areas
- get_area_params
- update_area
- move_area
- delete_area
- get_area_boundary_walls

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "refreshView": {
      "type": "boolean"
    },
    "toleranceMm": {
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
