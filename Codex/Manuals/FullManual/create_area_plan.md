# create_area_plan

- Category: Area
- Purpose: Create Area Plan in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_area_plan

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| areaSchemeId | unknown | no/depends |  |
| levelId | unknown | no/depends |  |
| name | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_area_plan",
  "params": {
    "areaSchemeId": "...",
    "levelId": "...",
    "name": "..."
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
    "name": {
      "type": "string"
    },
    "levelId": {
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
    "areaSchemeId": {
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
