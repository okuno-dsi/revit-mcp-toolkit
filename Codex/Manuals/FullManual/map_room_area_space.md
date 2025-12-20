# map_room_area_space

- Category: Rooms/Spaces
- Purpose: Given a Room, find the corresponding Area and/or MEP Space on the same level. Uses name/number match first, then nearest planar centroid (mm). Can emit only comparison factors for cross-document matching.

## Usage
- Method: `map_room_area_space`

### Parameters
- `roomId` (required): Room element id (int).
- `strategy` (optional): `"name_then_nearest"` (default) — try name/number equality on same level, fallback to nearest centroid.
- `emitFactorsOnly` (optional bool): If true, returns only `{ level, centroid }` for each mapped target.
- `maxDistanceMm` (optional): Distance threshold for nearest centroid fallback.

### Example Request (factors only)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "map_room_area_space",
  "params": { "roomId": 6809314, "emitFactorsOnly": true }
}
```

### Result Shape
```jsonc
{
  "ok": true,
  "result": {
    "room": { "id": 6809314, "level": "4FL", "name": "事務室4-2", "number": "80" },
    "area": { "id": 6812345, "level": "4FL", "name": "事務室4-2", "centroid": { "xMm": 12345.6, "yMm": 789.0 } },
    "space": { "id": 6806612, "level": "4FL", "name": "会議室2-5", "centroid": { "xMm": 12345.6, "yMm": 789.0 } }
  }
}
```

Notes
- Level matching is strict; name/number comparison is done after level alignment. If nothing matches, nearest centroid is used within `maxDistanceMm` if provided.
- With `emitFactorsOnly: true`, the response omits ids/names and keeps only `{ level, centroid }` for fast cross-port handoff.
- Units are millimeters.

## Related
- get_compare_factors
- find_similar_by_factors
