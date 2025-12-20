# copy_legend_components_between_views

- Category: AnnotationOps
- Purpose: Copy all Legend Components from one Legend view to another (no type-change, no layout).

## Overview
Copies `OST_LegendComponents` from a source Legend view to a target Legend view.

- Optional: clear target Legend components before copying.
- Does not change the copied components’ types.
- Does not reposition them (use `layout_legend_components_in_view` after copying).

## Usage
- Method: `copy_legend_components_between_views`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| sourceLegendViewName | string | no | DoorWindow_Legend_Template |
| baseLegendViewName | string | no | DoorWindow_Legend_Template |
| targetLegendViewName | string | no | DoorWindow_Legend_Elevations |
| clearTargetContents | bool | no | false |

Notes:
- `sourceLegendViewName` and `baseLegendViewName` are synonyms; `sourceLegendViewName` wins when both are set.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "copy_legend_components_between_views",
  "params": {
    "sourceLegendViewName": "DoorWindow_Legend_Template",
    "targetLegendViewName": "DoorWindow_Legend_Elevations",
    "clearTargetContents": true
  }
}
```

## Related
- create_legend_view_from_template
- layout_legend_components_in_view
- set_legend_component_type

