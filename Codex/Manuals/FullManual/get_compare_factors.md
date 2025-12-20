# get_compare_factors

- Category: Rooms/Spaces
- Purpose: Return lightweight comparison factors for elements so they can be matched across models/documents (ports). Factors include category, level, centroid, and bounding box sizes in mm.

## Usage
- Method: `get_compare_factors`

### Parameters
- `elementIds` (required): array of Revit element ids to evaluate. Example: `[6809314]`

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_compare_factors",
  "params": { "elementIds": [6809314] }
}
```

### Result Shape
```jsonc
{
  "ok": true,
  "result": {
    "items": [
      {
        "elementId": 6809314,
        "categoryId": -2000160,
        "category": "Rooms",
        "levelId": 12345,
        "level": "4FL",
        "centroid": { "xMm": 12345.6, "yMm": 789.0, "zMm": 10200.0 },
        "bbox": { "dxMm": 12000.0, "dyMm": 8000.0, "dzMm": 2800.0 }
      }
    ]
  }
}
```

Notes
- For Rooms, centroid is computed from the outer boundary loop; for Areas from region centroid; for Spaces from `Location` when available. Otherwise a bounding-box center is used.
- All linear units are millimeters.
- Use this together with `find_similar_by_factors` for cross-document matching.

## Related
- map_room_area_space
- find_similar_by_factors
- classify_points_in_room
