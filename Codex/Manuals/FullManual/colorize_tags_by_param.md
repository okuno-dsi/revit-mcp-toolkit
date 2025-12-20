# colorize_tags_by_param

- Category: VisualizationOps
- Purpose: Colorize annotation tags in the active view (or selection) based on a parameter value and a color mapping.

## Overview
This command applies per-element `OverrideGraphicSettings` to annotation tags (e.g., Room Tags, Door Tags) in the active view. It reads a string parameter from each tag and assigns colors according to a mapping table.

## Usage
- Method: colorize_tags_by_param

### Parameters
All parameters are optional; sensible defaults are applied.

| Name | Type | Required | Default |
|---|---|---|---|
| config.parameterName | string | no | "Comments" |
| config.targetCategories | string[] | no | ["OST_RoomTags","OST_DoorTags","OST_WindowTags","OST_GenericAnnotation"] |
| config.mappings | object | no | {} |
| config.defaultColor | array/int-object | no | null |
| config.readFromHost | bool | no | false |

- `config.mappings` is an object whose keys are substrings to search for (case-insensitive) and whose values are colors:
  - Example: `"FIRE": [255,0,0]`, `"ACCESSIBLE": [0,128,0]`
- `config.defaultColor` can be `[r,g,b]` or `{ "r": 0, "g": 0, "b": 255 }` and is used when no mapping key matches the parameter value. If the parameter is empty or missing and `defaultColor` is provided, that color is applied.
- `config.readFromHost` when `true` makes the command read the parameter from the **host element** of the tag (e.g., the Room or Door) first, and fall back to the tag's own parameter when the host does not have it.

The config object may be passed either under `config` or flattened at the top level (e.g., `parameterName`, `targetCategories`, `mappings`, `defaultColor`).

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "colorize_tags_by_param",
  "params": {
    "config": {
      "parameterName": "Comments",
      "targetCategories": [
        "OST_RoomTags",
        "OST_DoorTags",
        "OST_WindowTags"
      ],
      "mappings": {
        "FIRE": [255, 0, 0],
        "ACCESSIBLE": [0, 128, 0],
        "EXISTING": [128, 128, 128]
      },
      "defaultColor": [0, 0, 255]
    }
  }
}
```

Scope:
- If any elements are selected, only selected elements in the target categories are processed.
- If no selection, all elements in the active view whose category is one of `targetCategories` are processed.
- If the active view has a View Template applied and you do **not** detach it beforehand, no colors are applied and the result includes  
  `templateApplied: true`, `templateViewId`, `skippedDueToTemplate: true`, `errorCode: "VIEW_TEMPLATE_LOCK"`, and a message asking you to detach the template.

## Related
- reset_tag_colors
- set_visual_override
- clear_visual_override

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "config": {
      "type": "object"
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
