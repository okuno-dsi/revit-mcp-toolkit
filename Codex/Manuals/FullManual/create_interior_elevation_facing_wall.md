# create_interior_elevation_facing_wall

- Category: ViewOps
- Purpose: 部屋と壁を指定して、壁に正対した Interior Elevation を作成

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_interior_elevation_facing_wall

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| autoCrop | unknown | no/depends |  |
| hostViewId | int | no/depends | 0 |
| levelId | int | no/depends |  |
| levelName | string | no/depends |  |
| name | string | no/depends |  |
| offsetMm | number | no/depends | 800.0 |
| roomId | unknown | no/depends |  |
| roomUniqueId | unknown | no/depends |  |
| scale | int | no/depends | 100 |
| viewTypeId | int | no/depends | 0 |
| wallId | unknown | no/depends |  |
| wallUniqueId | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_interior_elevation_facing_wall",
  "params": {
    "autoCrop": "...",
    "hostViewId": 0,
    "levelId": 0,
    "levelName": "...",
    "name": "...",
    "offsetMm": 0.0,
    "roomId": "...",
    "roomUniqueId": "...",
    "scale": 0,
    "viewTypeId": 0,
    "wallId": "...",
    "wallUniqueId": "..."
  }
}
```

## Related
- get_current_view
- get_view_info
- save_view_state
- restore_view_state
- create_view_plan
- create_section
- create_elevation_view
- compare_view_states

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "levelId": {
      "type": "integer"
    },
    "scale": {
      "type": "integer"
    },
    "offsetMm": {
      "type": "number"
    },
    "hostViewId": {
      "type": "integer"
    },
    "roomId": {
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
    "viewTypeId": {
      "type": "integer"
    },
    "wallUniqueId": {
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
    "roomUniqueId": {
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
    "autoCrop": {
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
    "levelName": {
      "type": "string"
    },
    "wallId": {
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
    "name": {
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
