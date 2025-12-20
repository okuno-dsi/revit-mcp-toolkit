# auto_area_boundaries_from_walls

- Category: Area
- Purpose: Auto Area Boundaries From Walls in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: auto_area_boundaries_from_walls

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| batchSize | int | no/depends | 200 |
| includeCategories | unknown | no/depends |  |
| maxMillisPerTx | int | no/depends | 250 |
| mergeToleranceMm | number | no/depends | 5.0 |
| refreshView | bool | no/depends | false |
| startIndex | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "auto_area_boundaries_from_walls",
  "params": {
    "batchSize": 0,
    "includeCategories": "...",
    "maxMillisPerTx": 0,
    "mergeToleranceMm": 0.0,
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
    "mergeToleranceMm": {
      "type": "number"
    },
    "refreshView": {
      "type": "boolean"
    },
    "maxMillisPerTx": {
      "type": "integer"
    },
    "includeCategories": {
      "type": [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "array",
        "null"
      ]
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
