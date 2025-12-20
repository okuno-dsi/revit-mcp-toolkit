# update_area

- Category: Area
- Purpose: Update Area in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: update_area

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| batchSize | int | no/depends | 50 |
| maxMillisPerTx | int | no/depends | 100 |
| paramName | string | no/depends |  |
| refreshView | bool | no/depends | false |
| startIndex | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_area",
  "params": {
    "batchSize": 0,
    "maxMillisPerTx": 0,
    "paramName": "...",
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
- move_area
- delete_area
- get_area_boundary_walls
- get_area_centroid

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "startIndex": {
      "type": "integer"
    },
    "refreshView": {
      "type": "boolean"
    },
    "paramName": {
      "type": "string"
    },
    "batchSize": {
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
