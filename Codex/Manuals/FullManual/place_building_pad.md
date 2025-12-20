# place_building_pad

- Category: SiteOps
- Purpose: Place Building Pad in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: place_building_pad

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| levelName | string | no/depends |  |
| offsetMm | number | no/depends | 0.0 |
| padTypeName | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "place_building_pad",
  "params": {
    "levelName": "...",
    "offsetMm": 0.0,
    "padTypeName": "..."
  }
}
```

## Related
- create_toposurface_from_points
- append_toposurface_points
- replace_toposurface_points
- create_property_line_from_points
- set_project_base_point
- set_survey_point
- set_shared_coordinates_from_points
- get_site_overview

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "levelName": {
      "type": "string"
    },
    "offsetMm": {
      "type": "number"
    },
    "padTypeName": {
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
