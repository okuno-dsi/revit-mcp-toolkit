# get_spaces

- Category: Space
- Purpose: Get Spaces in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_spaces

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends |  |
| desc | bool | no/depends | false |
| includeCenter | bool | no/depends | true |
| includeParameters | bool | no/depends | false |
| levelId | int | no/depends |  |
| nameContains | string | no/depends |  |
| numberContains | string | no/depends |  |
| orderBy | string | no/depends | number |
| skip | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_spaces",
  "params": {
    "count": 0,
    "desc": false,
    "includeCenter": false,
    "includeParameters": false,
    "levelId": 0,
    "nameContains": "...",
    "numberContains": "...",
    "orderBy": "...",
    "skip": 0
  }
}
```

## Related
- create_space
- delete_space
- get_space_params
- move_space
- update_space
- get_space_boundary
- get_space_boundary_walls
- get_space_centroid

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "numberContains": {
      "type": "string"
    },
    "levelId": {
      "type": "integer"
    },
    "skip": {
      "type": "integer"
    },
    "orderBy": {
      "type": "string"
    },
    "includeCenter": {
      "type": "boolean"
    },
    "desc": {
      "type": "boolean"
    },
    "nameContains": {
      "type": "string"
    },
    "count": {
      "type": "integer"
    },
    "includeParameters": {
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
