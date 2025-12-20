# clean_area_boundaries

- Category: Area
- Purpose: Clean Area Boundaries in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: clean_area_boundaries

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| batchSize | int | no/depends |  |
| deleteIsolated | bool | no/depends | true |
| extendToleranceMm | number | no/depends | 50.0 |
| maxMillisPerTx | int | no/depends | 0 |
| mergeToleranceMm | number | no/depends | 5.0 |
| refreshView | bool | no/depends | false |
| stage | string | no/depends | all |
| startIndex | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "clean_area_boundaries",
  "params": {
    "batchSize": 0,
    "deleteIsolated": false,
    "extendToleranceMm": 0.0,
    "maxMillisPerTx": 0,
    "mergeToleranceMm": 0.0,
    "refreshView": false,
    "stage": "...",
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
    "batchSize": {
      "type": "integer"
    },
    "maxMillisPerTx": {
      "type": "integer"
    },
    "stage": {
      "type": "string"
    },
    "deleteIsolated": {
      "type": "boolean"
    },
    "extendToleranceMm": {
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
