# find_similar_by_factors

- Category: Rooms/Spaces
- Purpose: Find elements similar to given comparison factors within the current document. Intended for cross-port/model mapping using level + centroid and category filtering.

## Usage
- Method: `find_similar_by_factors`

### Parameters
- `categoryId` (recommended): Revit category id (e.g., Spaces = -2008044). If omitted, all categories are considered.
- `level` (recommended): Level name. If provided, search is constrained to elements on the same level.
- `centroid` (required): `{ "xMm": number, "yMm": number }` planar centroid in millimeters.
- `maxDistanceMm` (optional): Maximum planar distance in millimeters. Default: none.
- `top` (optional): Maximum number of results to return. Default: 5.

### Example Request (match a Space in MEP model)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "find_similar_by_factors",
  "params": {
    "categoryId": -2008044,
    "level": "4FL",
    "centroid": { "xMm": 12345.6, "yMm": 789.0 },
    "maxDistanceMm": 5000,
    "top": 5
  }
}
```

### Result Shape
```jsonc
{
  "ok": true,
  "result": {
    "items": [
      { "elementId": 6806612, "level": "4FL", "distanceMm": 120.4, "centroid": { "xMm": 12345.7, "yMm": 788.9 } }
    ]
  }
}
```

Notes
- Provide `categoryId` and `level` whenever possible for faster and more accurate matches.
- Distances are planar (XY) in mm.
- Use together with `get_compare_factors` produced from the source model.

## Related
- get_compare_factors
- map_room_area_space
