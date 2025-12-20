# move_area_boundary_line

- Category: Area
- Purpose: Move Area Boundary Line in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: move_area_boundary_line

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| batchSize | int | no/depends | 50 |
| dx | number | no/depends | 0 |
| dy | number | no/depends | 0 |
| dz | number | no/depends | 0 |
| elementId | int | no/depends |  |
| maxMillisPerTx | int | no/depends | 100 |
| refreshView | bool | no/depends | false |
| startIndex | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "move_area_boundary_line",
  "params": {
    "batchSize": 0,
    "dx": 0.0,
    "dy": 0.0,
    "dz": 0.0,
    "elementId": 0,
    "maxMillisPerTx": 0,
    "refreshView": false,
    "startIndex": 0
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
    "startIndex": {
      "type": "integer"
    },
    "elementId": {
      "type": "integer"
    },
    "refreshView": {
      "type": "boolean"
    },
    "maxMillisPerTx": {
      "type": "integer"
    },
    "batchSize": {
      "type": "integer"
    },
    "dz": {
      "type": "number"
    },
    "dy": {
      "type": "number"
    },
    "dx": {
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
