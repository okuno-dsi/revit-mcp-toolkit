# reset_tag_colors

- Category: VisualizationOps
- Purpose: Reset per-element overrides applied to annotation tags in the active view (or selection).

## Overview
This command clears `OverrideGraphicSettings` for tag elements (e.g., Room Tags, Door Tags) in the active view or current selection, restoring their default appearance.

## Usage
- Method: reset_tag_colors

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| targetCategories | string[] | no | ["OST_RoomTags","OST_DoorTags","OST_WindowTags","OST_GenericAnnotation"] |

If `targetCategories` is omitted, default tag categories are used. Categories are specified by `BuiltInCategory` names (e.g., `"OST_RoomTags"`).

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "reset_tag_colors",
  "params": {
    "targetCategories": [
      "OST_RoomTags",
      "OST_DoorTags"
    ]
  }
}
```

Scope:
- If any elements are selected, only selected elements in the target categories are reset.
- If no selection, all elements in the active view whose category is one of `targetCategories` are reset.

## Related
- colorize_tags_by_param
- clear_visual_override

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "targetCategories": {
      "type": "array"
    }
  },
  "additionalProperties": true
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

