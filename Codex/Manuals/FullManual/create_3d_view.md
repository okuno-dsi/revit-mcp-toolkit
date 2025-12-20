# create_3d_view

- Category: ViewOps
- Purpose: Create 3D View in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_3d_view

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| name | string | no/depends |  |
| templateViewId | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_3d_view",
  "params": {
    "name": "...",
    "templateViewId": 0
  }
}
```

## Notes (3D working views)
- For MCP-driven operations in 3D (color overrides, section boxes, element moves), prefer creating a **fresh isometric 3D view** via `create_3d_view` instead of duplicating an existing 3D view in the Revit UI.
- Copying an existing 3D view (especially one with a view template applied) can make it harder to:
  - hide categories (template restrictions),
  - see visual overrides applied by MCP,
  - understand which view MCP commands are targeting.
- A recommended pattern is:
  1. Call `create_3d_view` to create a dedicated working 3D view (e.g. `MCP_SB440_3D`).
  2. Run `set_visual_override`, `set_section_box_by_elements`, etc., **against that view**.

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
    "templateViewId": {
      "type": "integer"
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
