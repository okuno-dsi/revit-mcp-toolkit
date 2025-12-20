# delete_filled_region

- Category: Annotations/FilledRegion
- Purpose: Delete one or more FilledRegion elements.

## Overview
Deletes the specified `FilledRegion` instances from the document.  
Missing or invalid ids are ignored; the result reports how many regions were actually deleted.

## Usage
- Method: delete_filled_region

### Parameters
```jsonc
{
  "elementIds": [600001, 600002, 600003]
}
```

- `elementIds` (int[], required)
  - List of candidate element ids; non-FilledRegion elements are skipped.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": "region-delete",
  "method": "delete_filled_region",
  "params": {
    "elementIds": [600001, 600002]
  }
}
```

## Result
```jsonc
{
  "ok": true,
  "msg": "Deleted 2 filled regions.",
  "deletedCount": 2,
  "requestedCount": 2,
  "deletedElementIds": [600001, 600002]
}
```

## Notes

- The command uses a single transaction and attempts to delete all requested ids.
- Deleting a FilledRegion may also remove dependent annotation graphics; Revit may return multiple element ids for a single delete.
- Use `get_filled_regions_in_view` first if you need to confirm which regions exist in a view.

## Related
- get_filled_regions_in_view
- create_filled_region
- replace_filled_region_boundary

