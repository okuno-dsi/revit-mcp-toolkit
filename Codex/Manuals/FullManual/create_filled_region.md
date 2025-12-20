# create_filled_region

- Category: Annotations/FilledRegion
- Purpose: Create a FilledRegion in a view from polygon loops (mm coordinates).

## Overview
Creates a view-specific FilledRegion element using one or more polygon loops defined in millimeters.  
The region type controls the fill pattern and color; use `get_filled_region_types` to inspect available types.

## Usage
- Method: create_filled_region

### Parameters
```jsonc
{
  "viewId": 11120738,
  "typeId": 12345,              // optional; if omitted, uses the default FilledRegionType
  "foregroundPatternId": 2001,  // optional; FillPatternElement id
  "foregroundPatternName": "Solid fill",      // optional; used if id is not given
  "backgroundPatternId": 2002,  // optional
  "backgroundPatternName": "Diagonal up",     // optional
  "loops": [
    [
      { "x": 0, "y": 0, "z": 0 },
      { "x": 1000, "y": 0, "z": 0 },
      { "x": 1000, "y": 500, "z": 0 },
      { "x": 0, "y": 500, "z": 0 }
    ]
  ]
}
```

- `viewId` (int, required):
  - Target view id; must be a non-template view that allows annotations.
- `typeId` (int, optional):
  - `FilledRegionType` id to use; if omitted, the Revit default FilledRegionType is used.
- `foregroundPatternId` / `foregroundPatternName` (optional):
  - If specified, a duplicate of the base FilledRegionType is created and its foreground pattern is replaced
    with the given `FillPatternElement` (resolved by id or name).
- `backgroundPatternId` / `backgroundPatternName` (optional):
  - Same as above, but for the background pattern.
- `loops` (array, required):
  - Array of polygon loops. Each loop is an array of `{x,y,z}` points in mm.
  - Each loop must have 3 or more distinct points; the command automatically closes the loop.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": "region-rect",
  "method": "create_filled_region",
  "params": {
    "viewId": 11120738,
    "typeId": 12345,
    "loops": [
      [
        { "x": 0, "y": 0, "z": 0 },
        { "x": 1000, "y": 0, "z": 0 },
        { "x": 1000, "y": 500, "z": 0 },
        { "x": 0, "y": 500, "z": 0 }
      ]
    ]
  }
}
```

## Result
```jsonc
{
  "ok": true,
  "elementId": 600001,
  "uniqueId": "....",
  "typeId": 12345,
  "viewId": 11120738
}
```

## Notes

- Coordinates are interpreted in model space and converted from mm to Revit internal units (ft).
- For plan views, specify `z` in the same plane as the view (typically 0).
- This command does not currently support automatic holes; pass multiple loops if needed (outer + inner), but be aware of Revit’s constraints on loop orientation.
- When pattern overrides are provided, the command duplicates the base FilledRegionType with a new name (suffix `_McpFill`),
  applies the requested foreground/background patterns, and uses that duplicate type for the new region.

## Related
- get_filled_region_types
- get_filled_regions_in_view
- replace_filled_region_boundary
