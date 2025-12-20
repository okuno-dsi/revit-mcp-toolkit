# delete_site_subregion

- Category: SiteOps
- Purpose: Delete Site Subregion in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: delete_site_subregion

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| closed | bool | no/depends | true |
| elementId | unknown | no/depends |  |
| ids | unknown | no/depends |  |
| topographyId | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "delete_site_subregion",
  "params": {
    "closed": false,
    "elementId": "...",
    "ids": "...",
    "topographyId": 0
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
    "topographyId": {
      "type": "integer"
    },
    "closed": {
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
