# layout_legend_components_in_view

- Category: AnnotationOps
- Purpose: Reposition Legend Components in a Legend view into a grid layout.

## Overview
This command only moves existing `OST_LegendComponents` in a Legend view.
It does not create/copy components and does not change their types.

## Usage
- Method: `layout_legend_components_in_view`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| viewId | int | no | 0 |
| viewName | string | no |  |
| legendViewName | string | no |  |
| layout | object | no |  |

`layout`:
- `maxColumns` (int, default `4`)
- `cellWidth_mm` (number, default `2500.0`)
- `cellHeight_mm` (number, default `2500.0`)
- `startPoint` (object): `{ "x_mm": 0.0, "y_mm": 0.0 }`

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "layout_legend_components_in_view",
  "params": {
    "legendViewName": "DoorWindow_Legend_Elevations",
    "layout": {
      "maxColumns": 5,
      "cellWidth_mm": 2000.0,
      "cellHeight_mm": 2000.0,
      "startPoint": { "x_mm": 0.0, "y_mm": 0.0 }
    }
  }
}
```

## Related
- copy_legend_components_between_views
- create_door_window_legend_by_mark

