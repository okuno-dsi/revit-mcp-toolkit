# extend_area_boundary_line

- Category: Area
- Purpose: Extend Area Boundary Line in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: extend_area_boundary_line

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| batchSize | int | no/depends | 25 |
| lineId | int | no/depends |  |
| maxExtendMm | number | no/depends | 5000.0 |
| maxMillisPerTx | int | no/depends | 100 |
| refreshView | bool | no/depends | false |
| startIndex | int | no/depends | 0 |
| targetLineId | int | no/depends |  |
| toleranceMm | number | no/depends | 3.0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "extend_area_boundary_line",
  "params": {
    "batchSize": 0,
    "lineId": 0,
    "maxExtendMm": 0.0,
    "maxMillisPerTx": 0,
    "refreshView": false,
    "startIndex": 0,
    "targetLineId": 0,
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
    "startIndex": {
      "type": "integer"
    },
    "lineId": {
      "type": "integer"
    },
    "refreshView": {
      "type": "boolean"
    },
    "targetLineId": {
      "type": "integer"
    },
    "maxExtendMm": {
      "type": "number"
    },
    "toleranceMm": {
      "type": "number"
    },
    "maxMillisPerTx": {
      "type": "integer"
    },
    "batchSize": {
      "type": "integer"
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
