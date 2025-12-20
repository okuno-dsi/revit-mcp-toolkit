# set_legend_component_type

- Category: AnnotationOps
- Purpose: Change the referenced FamilySymbol (type) of a Legend Component element.

## Overview
Legend components do not reliably support `ChangeTypeId`. This command updates:
- `BuiltInParameter.LEGEND_COMPONENT` (ElementId)

## Usage
- Method: `set_legend_component_type`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| legendComponentId | int | no / one of |  |
| elementId | int | no / one of |  |
| targetTypeId | int | no / one of |  |
| typeId | int | no / one of |  |
| expectedCategory | string | no |  |

`expectedCategory` supports `"Doors"` / `"Windows"` for a lightweight sanity check.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_legend_component_type",
  "params": {
    "legendComponentId": 6000123,
    "targetTypeId": 7000456,
    "expectedCategory": "Doors"
  }
}
```

## Related
- copy_legend_components_between_views
- layout_legend_components_in_view

