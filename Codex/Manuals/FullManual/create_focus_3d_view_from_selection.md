# Create Focus 3D View (SectionBox) From Selection

Creates a new isometric 3D view, sets a **SectionBox** around the current selection (or provided `elementIds`), and optionally activates the new view.

This command exists to reduce latency vs. chaining multiple calls (`get_selected_element_ids` → `create_3d_view` → `activate_view` → `set_section_box_by_elements` → `view_fit`).

## Command
- Canonical: `view.create_focus_3d_view_from_selection`
- Aliases: `create_focus_3d_view_from_selection`, `create_clipping_3d_view_from_selection`

## Parameters
- `elementIds` (optional, `int[]`): target elements; defaults to current selection.
- `paddingMm` (optional, `double`, default `200`): padding around combined bounding box.
- `name` (optional, `string`): explicit view name.
- `viewNamePrefix` (optional, `string`, default `Clip3D_Selected_`): used when `name` is not provided.
- `activate` (optional, `bool`, default `true`): activate the created view.
- `templateViewId` (optional, `int`): apply a view template to the created view (may affect visibility/graphics).

## Example (JSON-RPC)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view.create_focus_3d_view_from_selection",
  "params": {
    "paddingMm": 200,
    "viewNamePrefix": "Clip3D_Selected_",
    "activate": true
  }
}
```

## Result (shape)
- `viewId`, `name`, `activated`
- `sectionBox`: `{min:{x,y,z}, max:{x,y,z}}` in **mm**
- `skipped`: elements ignored during bounding-box aggregation (e.g. no bounding box)

