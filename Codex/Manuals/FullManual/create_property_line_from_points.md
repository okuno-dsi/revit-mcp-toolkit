# create_property_line_from_points

- Category: SiteOps
- Purpose: Create Property Line From Points in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_property_line_from_points

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| closed | bool | no/depends | true |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_property_line_from_points",
  "params": {
    "closed": false
  }
}
```

## Related
- create_toposurface_from_points
- append_toposurface_points
- replace_toposurface_points
- place_building_pad
- set_project_base_point
- set_survey_point
- set_shared_coordinates_from_points
- get_site_overview

### Params Schema
```json
{
  "type": "object",
  "properties": {
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
