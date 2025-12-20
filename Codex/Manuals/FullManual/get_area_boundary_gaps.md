# get_area_boundary_gaps

- Category: Area
- Purpose: Get Area Boundary Gaps in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_area_boundary_gaps

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| toleranceMm | number | no/depends | 50.0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_area_boundary_gaps",
  "params": {
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
