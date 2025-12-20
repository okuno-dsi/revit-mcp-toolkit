# get_candidate_exterior_floors

- Category: Floors
- Purpose: Return floors that are near exterior walls and whose underside is not occupied by Rooms, i.e. “candidate exterior floors” (ground slabs, exterior decks).

## Overview
Scans floors in the current document (optionally filtered by view and/or level) and:

1. Detects exterior wall candidates (using `get_candidate_exterior_walls`) and builds their centerline segments in plan.
2. For each floor, samples a small grid of points above and below the slab and checks whether those points lie inside a vertically bounded Room
   (“room interior volume” inferred from Level/BaseOffset/UpperLimit/UnboundedHeight).
3. Floors that:
   - have Rooms above (typical occupied floor), and
   - have no Rooms below (especially on the lowest level), and
   - are within a configurable distance from exterior wall centerlines  
   are returned as “candidate exterior floors”.

The command is read-only and intended as a helper for downstream workflows (e.g., finish takeoff, visualization).

## Usage
- Method: `get_candidate_exterior_floors`

### Parameters
```jsonc
{
  "viewId": 123456,                        // optional, int
  "levelId": 200001,                       // optional, int
  "levelIds": [200001, 200002],            // optional, int[]
  "maxDistanceFromExteriorWallMm": 1500.0, // optional, double (default 1500.0)
  "minExteriorWallAreaM2": 1.0,            // optional, double (default 1.0)
  "sampleGrid": 3,                         // optional, int (1..7, default 3)
  "outsideThreshold": 0.5,                 // optional, double 0..1 (default 0.5)
  "useRooms": true                         // optional, bool (default true)
}
```

- `viewId` (int, optional)  
  If specified, only floors (and walls) visible in this view are considered.
- `levelId` (int, optional)  
  Single Level filter. Only floors whose `LevelId` matches this id are considered.
- `levelIds` (int[], optional)  
  Multiple Level filter; if provided and non-empty, it overrides `levelId`.
- `maxDistanceFromExteriorWallMm` (double, optional, default `1500.0`)  
  Maximum horizontal distance in millimeters from a floor’s bounding-box center to any exterior wall centerline.  
  Floors farther than this value are not treated as exterior candidates.
- `minExteriorWallAreaM2` (double, optional, default `1.0`)  
  Minimum total area (m²) of faces classified as exterior for a wall to be considered an “exterior wall candidate”.  
  Same idea as in `get_candidate_exterior_walls`.
- `sampleGrid` (int, optional, default `3`, range 1..7)  
  Controls how many XY sample points are taken within each floor’s bounding box: `sampleGrid x sampleGrid`.  
- `outsideThreshold` (double, optional, default `0.5`, range 0..1)  
  Threshold for judging “no Room below”. The floor underside is considered outside when the ratio of samples with Rooms below is low enough.
- `useRooms` (bool, optional, default `true`)  
  When `true`, Rooms are used to classify above/below occupancy via `Room.IsPointInRoom`.  
  When `false`, Rooms are ignored and all floors will be treated as if there were no Rooms; typically you want this `true`.

### Classification Logic (simplified)

For each floor:

- Compute `aboveRatio` = ratio of sample points just above the floor that fall inside a room interior volume.
- Compute `belowRatio` = ratio of sample points just below the floor that fall inside a room interior volume.
- Determine whether there is a level below the floor’s Level.

The floor is treated as a candidate exterior floor if:

- There are Rooms above (`aboveRatio >= 0.5`), and
- There are effectively no Rooms below:
  - On the lowest level: `belowRatio == 0.0`
  - Otherwise: `belowRatio` is low, based on `outsideThreshold`, and
- The floor’s bounding-box center is within `maxDistanceFromExteriorWallMm` of at least one exterior wall centerline.

## Result
```jsonc
{
  "ok": true,
  "msg": null,
  "totalCount": 5,
  "floors": [
    {
      "elementId": 61230001,
      "id": 61230001,
      "uniqueId": "f8f6455a-...",
      "levelId": 60521762,
      "typeId": 54010001,
      "typeName": "EXT-SLAB-150",
      "distanceFromExteriorWallMm": 420.5,
      "roomAboveRatio": 1.0,
      "roomBelowRatio": 0.0,
      "isLowestLevel": true
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
- `msg` (string|null): Optional message; e.g., when no exterior walls are found.
- `totalCount` (int): Number of floors returned in `floors`.
- `floors` (array): Candidate exterior floors.
  - `elementId` / `id`: Floor `ElementId` (int).
  - `uniqueId`: Revit `UniqueId` string.
  - `levelId`: Level `ElementId` (int) for this floor (0 if unknown).
  - `typeId`: Floor type `ElementId` (int).
  - `typeName`: Floor type name, if available.
  - `distanceFromExteriorWallMm`: Shortest distance from the floor’s center to any exterior wall centerline.
  - `roomAboveRatio`: Ratio of samples above the floor that are inside Rooms.
  - `roomBelowRatio`: Ratio of samples below the floor that are inside Rooms.
  - `isLowestLevel`: `true` when there is no level below this floor’s level.

## Notes

- Exterior wall detection:
  - Uses `get_candidate_exterior_walls`, which itself relies on geometric sampling along wall baselines and Room-based interior volume checks.
  - The exterior area used here (`minExteriorWallAreaM2`) is an approximation (baseline length × height) and is meant as a coarse filter for walls.
- Floor occupancy detection:
  - Uses bounding boxes and vertical sampling against Room interior volumes. It is an **approximate heuristic**, not a guarantee:
    - Works reasonably well to distinguish
      - interior slabs between Rooms (Rooms above and below),
      - exterior ground slabs (Rooms above, none below),
      - some roof/deck cases (Rooms below only) depending on how Rooms are modeled.
    - On very irregular buildings (deep overhangs, courtyards, heavy articulation) or where Rooms are missing, misclassifications are more likely.
- This command is best used as a **candidate finder** for downstream review or visualization, not as a definitive classification of all slabs.
- Combine with:
  - `get_candidate_exterior_walls`
  - `get_candidate_exterior_columns`
  - `scan_paint_and_split_regions`, `get_face_paint_data`
  - `create_filled_region`, `apply_paint_by_reference`
  to build more elaborate exterior-only finish workflows.
