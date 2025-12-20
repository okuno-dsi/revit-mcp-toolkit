# replace_filled_region_boundary

- Category: Annotations/FilledRegion
- Purpose: Replace the boundary (shape) of an existing FilledRegion.

## Overview
Replaces the polygon loops that define a `FilledRegion` with new ones, using the same view and type.  
Internally, the command creates a new FilledRegion with the new loops and deletes the old one.

## Usage
- Method: replace_filled_region_boundary

### Parameters
```jsonc
{
  "elementId": 600001,
  "loops": [
    [
      { "x": 0, "y": 0, "z": 0 },
      { "x": 1200, "y": 0, "z": 0 },
      { "x": 1200, "y": 600, "z": 0 },
      { "x": 0, "y": 600, "z": 0 }
    ]
  ]
}
```

- `elementId` (int, required)
  - Existing FilledRegion element id to replace.
- `loops` (array, required)
  - New polygon loops in mm, same format as `create_filled_region`.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": "region-replace-boundary",
  "method": "replace_filled_region_boundary",
  "params": {
    "elementId": 600001,
    "loops": [
      [
        { "x": 0, "y": 0, "z": 0 },
        { "x": 1200, "y": 0, "z": 0 },
        { "x": 1200, "y": 600, "z": 0 },
        { "x": 0, "y": 600, "z": 0 }
      ]
    ]
  }
}
```

## Result
```jsonc
{
  "ok": true,
  "oldElementId": 600001,
  "newElementId": 600010,
  "newUniqueId": "....",
  "viewId": 11120738
}
```

## Notes

- The new region uses the same `FilledRegionType` and owner view as the original.
- Because a new element is created, the `elementId` and `uniqueId` will change; update any stored references.
- If no valid loops (3+ distinct points) are provided, the command fails with `ok: false`.

## Related
- create_filled_region
- delete_filled_region
- get_filled_regions_in_view

