# get_space_centroid

- Category: Space
- Purpose: Get Space Centroid in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_space_centroid

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | unknown | no/depends |  |
| includeBoundingBox | bool | no/depends | false |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_space_centroid",
  "params": {
    "elementId": "...",
    "includeBoundingBox": false
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
- get_space_boundary_walls

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "includeBoundingBox": {
      "type": "boolean"
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
