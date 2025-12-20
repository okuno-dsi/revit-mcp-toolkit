# get_candidate_exterior_columns

- Category: Columns
- Purpose: Return structural/architectural columns that are likely to be exterior columns, based on their distance from exterior walls.

## Overview
Scans columns in the current document (optionally filtered by view and/or level) and checks how close they are, in plan (XY), to walls that are classified as exterior candidates.  
Columns whose plan location is within a configurable distance from these walls are returned as “candidate exterior columns”.

This command is read-only and is meant for bulk workflows such as:
- “List all columns near the building perimeter.”
- “Use only perimeter columns when generating a specific schedule or visualization.”

## Usage
- Method: `get_candidate_exterior_columns`

### Parameters
```jsonc
{
  "viewId": 123456,                 // optional, int
  "levelId": 200001,                // optional, int
  "levelIds": [200001, 200002],     // optional, int[]
  "maxDistanceMm": 1000.0,          // optional, double (default 1000.0)
  "minExteriorWallAreaM2": 1.0,     // optional, double (default 1.0)
  "includeArchitecturalColumns": true,  // optional, bool (default true)
  "includeStructuralColumns": true      // optional, bool (default true)
}
```

- `viewId` (int, optional)  
  If specified, only columns (and walls) visible in this view are considered.
- `levelId` (int, optional)  
  Single Level filter. Only columns whose `LevelId` matches this id are considered.
- `levelIds` (int[], optional)  
  Multiple Level filter; if provided and non-empty, it overrides `levelId`.
- `maxDistanceMm` (double, optional, default `1000.0`)  
  Maximum horizontal distance in millimeters from a column to any exterior wall centerline.  
  If the shortest distance is less than or equal to this value, the column is treated as an exterior candidate.
- `minExteriorWallAreaM2` (double, optional, default `1.0`)  
  Minimum total area (in m²) of faces classified as exterior for a wall to be treated as an “exterior wall candidate”.  
  This is the same idea as in `get_candidate_exterior_walls`.
- `includeArchitecturalColumns` (bool, optional, default `true`)  
  When `true`, includes `OST_Columns` (architectural columns).
- `includeStructuralColumns` (bool, optional, default `true`)  
  When `true`, includes `OST_StructuralColumns` (structural columns).

## Result
```jsonc
{
  "ok": true,
  "msg": null,
  "totalCount": 8,
  "columns": [
    {
      "elementId": 61234567,
      "id": 61234567,
      "uniqueId": "f8f6455a-...",
      "levelId": 60521762,
      "category": "StructuralColumns",
      "typeId": 54000001,
      "typeName": "C-600x600",
      "distanceMm": 350.0
    }
  ],
  "inputUnits": {
    "Length": "mm",
    "Area": "m2",
    "Volume": "m3",
    "Angle": "deg"
  },
  "internalUnits": {
    "Length": "ft",
    "Area": "ft2",
    "Volume": "ft3",
    "Angle": "rad"
  }
}
```

- `ok` (bool): `true` on success; `false` if a global error occurred.
- `msg` (string|null): Optional message; for example when no exterior walls are found.
- `totalCount` (int): Number of columns returned in `columns`.
- `columns` (array): Candidate exterior columns.
  - `elementId` / `id`: Column `ElementId` (int).
  - `uniqueId`: Revit `UniqueId` string.
  - `levelId`: Level `ElementId` (int) for this column (0 if unknown).
  - `category`: `"StructuralColumns"` or `"Columns"` (architectural).
  - `typeId`: Column type `ElementId` (int).
  - `typeName`: Column type name, if available.
  - `distanceMm`: Shortest horizontal distance in mm from the column to any exterior wall centerline.

## Notes

- Exterior walls are detected using `get_candidate_exterior_walls`, which itself relies on geometric sampling along wall baselines and Room-based
  interior volume checks. The exterior area used here is an approximation intended as a coarse filter for walls.
- Column positions are taken from `LocationPoint` when available; if not, the element’s bounding-box center is used as a fallback.
- Distances are computed in the XY plane (plan view) to the wall centerlines:
  - This is an approximation that generally works well for “near perimeter” detection, but on very irregular buildings (deep recesses, courtyards,
    complex podiums) you should expect some misclassifications.
- Overall this command is best used as a **candidate finder**; for critical use‑cases you should visually review/confirm the resulting columns
  against the model.
- For more detailed face-level analysis or paint logic, combine this command with:
  - `get_candidate_exterior_walls`
  - `classify_wall_faces_by_side`
  - `get_face_paint_data`, `apply_paint_by_reference`
