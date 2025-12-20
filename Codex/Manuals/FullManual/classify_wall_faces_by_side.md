# classify_wall_faces_by_side

- Category: Walls
- Purpose: Classify each face of a wall as exterior, interior, top, bottom, end, or other, with optional geometry info and stable references.

## Overview
Analyzes the geometry of one or more wall elements and returns a per-face classification.  
The result can be combined with paint commands (for example `scan_paint_and_split_regions`, `apply_paint_by_reference`, `remove_paint_by_reference`) to selectively keep or clear paint on specific sides of walls.

## Usage
- Method: `classify_wall_faces_by_side`

### Parameters
```jsonc
{
  "elementIds": [6799522, 6799523],  // required, wall element ids
  "offsetMm": 1000.0,                // optional, default 1000.0
  "roomCheck": true,                 // optional, default true
  "minAreaM2": 1.0,                  // optional, default 1.0
  "includeGeometryInfo": false,      // optional, default false
  "includeStableReference": true     // optional, default true
}
```

- `elementIds` (int[], required)  
  List of wall `ElementId` values to analyze. Non-wall elements are returned in `errors`.
- `offsetMm` (double, optional, default `1000.0`)  
  Offset distance in millimeters used when checking which side of the wall is inside a Room.  
  The command samples two points along the face normal:
  - `pOut = p + n * offsetFt`  
  - `pIn  = p - n * offsetFt`  
  where `offsetFt` is converted from millimeters to Revit internal units (feet).
- `roomCheck` (bool, optional, default `true`)  
  When `true`, Room containment is used to decide whether a vertical face is interior or exterior.  
  When `false`, only the wall orientation and host-side information are used.
- `minAreaM2` (double, optional, default `1.0`)  
  Minimum area (in m²) for vertical faces to be considered side faces.  
  Vertical faces smaller than this are classified as `end`.
- `includeGeometryInfo` (bool, optional, default `false`)  
  When `true`, the response includes approximate face area (in m²) and a normal vector for each face.
- `includeStableReference` (bool, optional, default `true`)  
  When `true`, the response includes `stableReference` strings for faces.  
  These can be passed into commands like `apply_paint_by_reference` or `get_face_paint_data`.

### Example Request
```jsonc
{
  "jsonrpc": "2.0",
  "id": "classify-wall-faces",
  "method": "classify_wall_faces_by_side",
  "params": {
    "elementIds": [6799522],
    "offsetMm": 1000.0,
    "roomCheck": true,
    "minAreaM2": 1.0,
    "includeGeometryInfo": true,
    "includeStableReference": true
  }
}
```

## Result
```jsonc
{
  "ok": true,
  "walls": [
    {
      "elementId": 6799522,
      "faces": [
        {
          "faceIndex": 0,
          "role": "exterior",
          "isVertical": true,
          "areaM2": 12.34,
          "normal": { "x": 0.0, "y": -1.0, "z": 0.0 },
          "stableReference": "abcd-efgh-..."
        },
        {
          "faceIndex": 1,
          "role": "interior",
          "isVertical": true,
          "areaM2": 11.98,
          "normal": { "x": 0.0, "y": 1.0, "z": 0.0 },
          "stableReference": "ijkl-mnop-..."
        },
        {
          "faceIndex": 2,
          "role": "top",
          "isVertical": false,
          "areaM2": 12.00,
          "normal": { "x": 0.0, "y": 0.0, "z": 1.0 },
          "stableReference": "qrst-uvwx-..."
        }
      ]
    }
  ],
  "errors": []
}
```

- `ok` (bool): `true` on success, `false` if there was a global error.
- `walls` (array): One entry per wall that was successfully analyzed.
- `errors` (array): Zero or more entries like:
  ```jsonc
  {
    "elementId": 12345,
    "message": "Element is not a Wall."
  }
  ```

Each face entry:
- `faceIndex`: Index of the planar face. For consistency, this aligns with indices returned by `get_wall_faces`.
- `role`: One of:
  - `"exterior"`: vertical face classified as outside of the wall.
  - `"interior"`: vertical face classified as inside (toward Rooms).
  - `"top"`: upward-facing horizontal face.
  - `"bottom"`: downward-facing horizontal face.
  - `"end"`: small vertical faces (wall ends) below `minAreaM2`.
  - `"other"`: faces that do not fit any category or could not be classified reliably.
- `isVertical`: `true` for near-vertical faces, `false` for near-horizontal faces.
- `areaM2` (optional): Face area in square meters, rounded when `includeGeometryInfo == true`.
- `normal` (optional): Approximate unit normal vector `{x, y, z}`, when `includeGeometryInfo == true`.
- `stableReference` (optional): Stable face reference string when `includeStableReference == true`.

## Notes

- The command is read-only and does not change any geometry or materials.
- Room-based classification (when `roomCheck == true`) takes precedence over host-side and orientation heuristics.
- For very complex walls, you can increase `minAreaM2` to treat more small faces as `end`.
- This command is designed to be combined with:
  - `scan_paint_and_split_regions`
  - `get_face_paint_data`
  - `apply_paint_by_reference`
  - `remove_paint_by_reference`

