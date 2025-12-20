# create_area

- Category: Area
- Purpose: Create an Area (in an Area Plan view) at a specified XY point.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

Important
- Areas belong to an **AreaScheme**. If your project has multiple AreaSchemes, using only `levelId` can create an Area in the *wrong* scheme (because multiple AreaPlan views can exist per level).
- To target the intended AreaScheme reliably, pass the **Area Plan view** via `viewId` (recommended) or `viewUniqueId`.

## Usage
- Method: create_area

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| viewId | int | no* | active/level lookup |
| viewUniqueId | string | no* | active/level lookup |
| batchSize | int | no/depends | 50 |
| levelId | int | no/depends |  |
| maxMillisPerTx | int | no/depends | 100 |
| refreshView | bool | no/depends | false |
| startIndex | int | no/depends | 0 |

*Provide either `viewId/viewUniqueId` (recommended) or `levelId`.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_area",
  "params": {
    "viewId": 11120260,
    "x": 21576.985,
    "y": 5852.247,
    "refreshView": true
  }
}
```

## Related
- get_area_boundary
- get_areas
- get_area_params
- update_area
- move_area
- delete_area
- get_area_boundary_walls
- get_area_centroid

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
    "maxMillisPerTx": {
      "type": "integer"
    },
    "levelId": {
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
