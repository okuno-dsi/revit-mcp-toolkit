# set_filled_region_type

- Category: Annotations/FilledRegion
- Purpose: Change the FilledRegionType (pattern/color) for existing FilledRegion elements.

## Overview
Updates one or more `FilledRegion` instances to use a different `FilledRegionType`.  
Use this when you want to switch hatch pattern or color without recreating the regions.

## Usage
- Method: set_filled_region_type

### Parameters
```jsonc
{
  "elementIds": [600001, 600002],
  "typeId": 12345
}
```

- `elementIds` (int[], required)
  - List of FilledRegion element ids to update.
- `typeId` (int, required)
  - Target `FilledRegionType` id. You can discover candidates via `get_filled_region_types`.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": "region-type-swap",
  "method": "set_filled_region_type",
  "params": {
    "elementIds": [600001, 600002],
    "typeId": 12345
  }
}
```

## Result
```jsonc
{
  "ok": true,
  "msg": "Updated 2 regions. 0 regions failed.",
  "stats": {
    "successCount": 2,
    "failureCount": 0
  },
  "results": [
    { "elementId": 600001, "ok": true },
    { "elementId": 600002, "ok": true }
  ]
}
```

## Notes

- The command runs in a single transaction and skips invalid ids or non-FilledRegion elements.
- If the `typeId` does not exist in the document, the whole call fails with `ok: false`.
- Geometry (boundary) is unchanged; only the visual style defined by the type is updated.

## Related
- get_filled_region_types
- get_filled_regions_in_view
- create_filled_region

