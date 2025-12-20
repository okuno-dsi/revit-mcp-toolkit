# get_areas

- Category: Area
- Purpose: Get Areas in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_areas

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| areaMaxM2 | number | no/depends |  |
| areaMinM2 | number | no/depends |  |
| count | int | no/depends |  |
| desc | bool | no/depends | false |
| includeCentroid | bool | no/depends | false |
| includeParameters | bool | no/depends | false |
| levelId | int | no/depends |  |
| nameContains | string | no/depends |  |
| numberContains | string | no/depends |  |
| orderBy | string | no/depends | id |
| skip | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_areas",
  "params": {
    "areaMaxM2": 0.0,
    "areaMinM2": 0.0,
    "count": 0,
    "desc": false,
    "includeCentroid": false,
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
- get_area_boundary
- create_area
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
    "numberContains": {
      "type": "string"
    },
    "areaMinM2": {
      "type": "number"
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
    "desc": {
      "type": "boolean"
    },
    "nameContains": {
      "type": "string"
    },
    "count": {
      "type": "integer"
    },
    "includeCentroid": {
      "type": "boolean"
    },
    "includeParameters": {
      "type": "boolean"
    },
    "areaMaxM2": {
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
