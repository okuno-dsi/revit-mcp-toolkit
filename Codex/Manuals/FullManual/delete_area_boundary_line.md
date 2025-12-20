# delete_area_boundary_line

- Category: Area
- Purpose: Delete Area Boundary Line in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: delete_area_boundary_line

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| batchSize | int | no/depends | 50 |
| elementId | int | no/depends |  |
| maxMillisPerTx | int | no/depends | 100 |
| refreshView | bool | no/depends | false |
| startIndex | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "delete_area_boundary_line",
  "params": {
    "batchSize": 0,
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
    "refreshView": {
      "type": "boolean"
    },
    "startIndex": {
      "type": "integer"
    },
    "batchSize": {
      "type": "integer"
    },
    "elementId": {
      "type": "integer"
    },
    "maxMillisPerTx": {
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
