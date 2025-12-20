# set_topography_material

- Category: SiteOps
- Purpose: Set Topography Material in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: set_topography_material

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| materialName | string | no/depends |  |
| subregionId | int | no/depends |  |
| topographyId | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_topography_material",
  "params": {
    "materialName": "...",
    "subregionId": 0,
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
    "subregionId": {
      "type": "integer"
    },
    "materialName": {
      "type": "string"
    },
    "topographyId": {
      "type": "integer"
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
