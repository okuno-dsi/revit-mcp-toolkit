# get_space_boundary

- Category: Space
- Purpose: Get **calculated** Space boundary segments (mm) via `GetBoundarySegments`.

## Overview
This is an **A) calculated boundary** command (see `Manuals/FullManual/spatial_boundary_location.md`).

## Usage
- Method: get_space_boundary

### Parameters
| Name | Type | Required | Default |
|---|---:|:---:|---|
| elementId | int | yes |  |
| boundaryLocation | string | no | `Finish` |
| includeElementInfo | bool | no | false |

`boundaryLocation`: `Finish` / `Center` / `CoreCenter` / `CoreBoundary`

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_space_boundary",
  "params": {
    "elementId": 123456,
    "boundaryLocation": "Finish",
    "includeElementInfo": false
  }
}
```

## Related
- create_space
- delete_space
- get_space_params
- get_spaces
- move_space
- update_space
- get_space_boundary_walls
- get_space_centroid

Notes:
- Response includes `boundaryLocation` and `units`.

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "elementId": {
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
    "includeElementInfo": {
      "type": "boolean"
    },
    "boundaryLocation": {
      "type": "string"
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
