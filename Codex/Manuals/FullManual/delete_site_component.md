# delete_site_component

- Category: SiteOps
- Purpose: Delete Site Component in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: delete_site_component

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| angleDeg | number | no/depends | 0.0 |
| category | string | no/depends |  |
| elementId | unknown | no/depends |  |
| familyName | string | no/depends |  |
| ids | unknown | no/depends |  |
| levelName | string | no/depends |  |
| typeName | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "delete_site_component",
  "params": {
    "angleDeg": 0.0,
    "category": "...",
    "elementId": "...",
    "familyName": "...",
    "ids": "...",
    "levelName": "...",
    "typeName": "..."
  }
}
```

## Related
- create_toposurface_from_points
- append_toposurface_points
- replace_toposurface_points
- place_building_pad
- create_property_line_from_points
- set_project_base_point
- set_survey_point
- set_shared_coordinates_from_points

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
    "angleDeg": {
      "type": "number"
    },
    "typeName": {
      "type": "string"
    },
    "familyName": {
      "type": "string"
    },
    "levelName": {
      "type": "string"
    },
    "ids": {
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
    "category": {
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
