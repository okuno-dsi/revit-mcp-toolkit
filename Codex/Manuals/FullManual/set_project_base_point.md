# set_project_base_point

- Category: SiteOps
- Purpose: Set Project Base Point in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: set_project_base_point

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| angleToTrueNorthDeg | number | no/depends | 0.0 |
| sharedSiteName | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_project_base_point",
  "params": {
    "angleToTrueNorthDeg": 0.0,
    "sharedSiteName": "..."
  }
}
```

## Related
- create_toposurface_from_points
- append_toposurface_points
- replace_toposurface_points
- place_building_pad
- create_property_line_from_points
- set_survey_point
- set_shared_coordinates_from_points
- get_site_overview

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "angleToTrueNorthDeg": {
      "type": "number"
    },
    "sharedSiteName": {
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
