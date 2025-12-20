# get_space_boundary_walls

- Category: Space
- Purpose: Get Wall ids that appear in the **calculated** Space boundary.

## Overview
This is an **A) calculated boundary** command (see `Manuals/FullManual/spatial_boundary_location.md`).

## Usage
- Method: get_space_boundary_walls

### Parameters
| Name | Type | Required | Default |
|---|---:|:---:|---|
| elementId | int | yes |  |
| boundaryLocation | string | no | `Finish` |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_space_boundary_walls",
  "params": {
    "elementId": 123456,
    "boundaryLocation": "Finish"
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
- get_space_boundary
- get_space_centroid

Notes:
- Response includes `boundaryLocation` and `units`.

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "boundaryLocation": {
      "type": "string"
    },
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
