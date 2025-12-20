# get_area_params

- Category: Area
- Purpose: Get Area Params in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_area_params

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| areaId | unknown | no/depends |  |
| count | int | no/depends |  |
| desc | bool | no/depends | false |
| includeDisplay | bool | no/depends | true |
| includeRaw | bool | no/depends | true |
| includeUnit | bool | no/depends | true |
| nameContains | string | no/depends |  |
| orderBy | string | no/depends | name |
| skip | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_area_params",
  "params": {
    "areaId": "...",
    "count": 0,
    "desc": false,
    "includeDisplay": false,
    "includeRaw": false,
    "includeUnit": false,
    "nameContains": "...",
    "orderBy": "...",
    "skip": 0
  }
}
```

## Related
- get_area_boundary
- create_area
- get_areas
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
    "nameContains": {
      "type": "string"
    },
    "skip": {
      "type": "integer"
    },
    "orderBy": {
      "type": "string"
    },
    "includeDisplay": {
      "type": "boolean"
    },
    "areaId": {
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
    "includeRaw": {
      "type": "boolean"
    },
    "count": {
      "type": "integer"
    },
    "includeUnit": {
      "type": "boolean"
    },
    "desc": {
      "type": "boolean"
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
