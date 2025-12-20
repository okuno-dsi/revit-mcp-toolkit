# get_fill_patterns

- Category: Document/Patterns
- Purpose: List all fill patterns (FillPatternElement) available in the current project.

## Overview
Returns a list of `FillPatternElement` definitions in the active document.  
You can use this to discover pattern names and ids before creating or modifying FilledRegion types.

## Usage
- Method: get_fill_patterns

### Parameters
```jsonc
{
  "target": "Drafting",     // optional: "Drafting" | "Model" | "Any" (default: "Any")
  "solidOnly": true,        // optional: true = solid only, false = non-solid only, omitted = both
  "nameContains": "Solid"   // optional: case-insensitive substring match
}
```

- `target` (string, optional)
  - `"Drafting"`: drafting patterns only.
  - `"Model"`: model patterns only.
  - `"Any"` or omitted: no target filter.
- `solidOnly` (bool, optional)
  - `true`: include only solid fill patterns.
  - `false`: include only non-solid patterns.
  - omitted: include both solid and non-solid.
- `nameContains` (string, optional)
  - Filters patterns whose `name` contains the given substring (case-insensitive).

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": "fillpatterns-solid-drafting",
  "method": "get_fill_patterns",
  "params": {
    "target": "Drafting",
    "solidOnly": true
  }
}
```

## Result
```jsonc
{
  "ok": true,
  "totalCount": 3,
  "items": [
    {
      "elementId": 2001,
      "name": "Solid fill",
      "isSolid": true,
      "target": "Drafting"
    }
  ]
}
```

## Notes

- `elementId` corresponds to `FillPatternElement.Id.IntegerValue`; use this when specifying `foregroundPatternId` / `backgroundPatternId` for `create_filled_region`.
- `name` is the pattern name shown in Revit’s fill pattern dialog.
- `target` is the Revit `FillPatternTarget` value (`Drafting` or `Model`).

## Related
- get_filled_region_types
- create_filled_region

