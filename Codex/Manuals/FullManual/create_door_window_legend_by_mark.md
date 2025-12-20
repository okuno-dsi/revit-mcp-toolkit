# create_door_window_legend_by_mark

- Category: AnnotationOps
- Purpose: Place Door/Window legend components in a Legend view, grouped by Type Mark, with optional labels.

## Overview
Builds a Door/Window legend by scanning FamilySymbols and placing one legend component per `mark` (Type Mark by default), laid out in a grid.

Implementation notes:
- This command assumes you already have a Legend view to populate (e.g. `DoorWindow_Legend_Elevations`).
- It uses reflection to call `Autodesk.Revit.DB.LegendComponent.Create(...)` when available; in some Revit builds, this API may be unavailable and no components will be placed.

## Usage
- Method: `create_door_window_legend_by_mark`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| legendViewName | string | no | DoorWindow_Legend_Elevations |
| markParameter | string | no | Type Mark |
| includeDoors | bool | no | true |
| includeWindows | bool | no | true |
| clearExisting | bool | no | true |
| layout | object | no |  |
| labelOptions | object | no |  |

`layout`:
- `maxColumns` (int, default `5`)
- `cellWidth_mm` (number, default `2000.0`)
- `cellHeight_mm` (number, default `2000.0`)
- `startPoint` (object): `{ "x_mm": 0.0, "y_mm": 0.0 }`

`labelOptions`:
- `showMark` (bool, default `true`)
- `showSize` (bool, default `true`)
- `showFamilyType` (bool, default `true`)

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_door_window_legend_by_mark",
  "params": {
    "legendViewName": "DoorWindow_Legend_Elevations",
    "markParameter": "Type Mark",
    "includeDoors": true,
    "includeWindows": true,
    "clearExisting": true,
    "layout": {
      "maxColumns": 5,
      "cellWidth_mm": 2000.0,
      "cellHeight_mm": 2000.0,
      "startPoint": { "x_mm": 0.0, "y_mm": 0.0 }
    },
    "labelOptions": {
      "showMark": true,
      "showSize": true,
      "showFamilyType": true
    }
  }
}
```

## Related
- get_door_window_types_for_schedule
- create_legend_view_from_template
- copy_legend_components_between_views
- layout_legend_components_in_view

