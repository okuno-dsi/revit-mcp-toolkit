# create_toposurface_from_points

- Category: SiteOps
- Purpose: Create Toposurface From Points in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_toposurface_from_points

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| siteName | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_toposurface_from_points",
  "params": {
    "siteName": "..."
  }
}
```

## Related
- append_toposurface_points
- replace_toposurface_points
- place_building_pad
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
    "siteName": {
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
