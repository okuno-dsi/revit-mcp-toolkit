# get_candidate_exterior_walls

- Category: Walls
- Purpose: Return a list of walls that are likely to be exterior walls, based on face classification and optional room checks.

## Overview
Scans walls in the current document (optionally filtered by view and/or level) and classifies them based on their relationship to Rooms.  
The current implementation samples points along each wall’s `LocationCurve` (baseline) and, using the wall orientation, probes on both sides to see
whether those points lie inside a “room interior volume” (a Room vertically bounded by floor/roof, estimated from Level/BaseOffset/UpperLimit/etc.).  
If, along the baseline, points on one side consistently fall inside such a Room volume while the opposite side does not, the wall is returned as a
“candidate exterior wall”.

This is a read-only helper for bulk workflows, for example:
- “List all exterior walls in this building.”
- “Feed these wall ids into follow-up paint/finish commands.”

## Usage
- Method: `get_candidate_exterior_walls`

### Parameters
```jsonc
{
  "viewId": 123456,            // optional, int
  "levelId": 200001,           // optional, int
  "levelIds": [200001,200002], // optional, int[]
  "minExteriorAreaM2": 1.0,    // optional, double (default 1.0)
  "roomCheck": true,           // optional, bool (default true)
  "offsetMm": 1000.0           // optional, double (default 1000.0)
}
```

- `viewId` (int, optional)  
  If specified, only walls visible in this view are considered.
- `levelId` (int, optional)  
  Single Level filter. Only walls whose `LevelId` matches this id are considered.
- `levelIds` (int[], optional)  
  Multiple Level filter; if provided and non-empty, it overrides `levelId`.  
  Only walls whose `LevelId` is in this set are considered.
- `minExteriorAreaM2` (double, optional, default `1.0`)  
  Minimum approximate exterior area (in m²) for a wall to be included in the result.  
  The area is estimated as `baseline length × wall height` for walls that pass the Room-based exterior test.
- `roomCheck` (bool, optional, default `true`)  
  When `true`, Room containment (including vertical bounds inferred from Levels/BaseOffset/UpperLimit) is used to decide interior vs exterior.  
  When `false`, the command will treat all walls as interior (no exterior candidates).
- `offsetMm` (double, optional, default `1000.0`)  
  Offset distance (mm) used when performing the Room-based inside/outside check.  
  Two points are sampled along each face normal:
  - `pOut = p + n * offsetFt`  
  - `pIn  = p - n * offsetFt`  
  where `offsetFt` is converted from millimeters to internal feet.

## Result
```jsonc
{
  "ok": true,
  "msg": null,
  "totalCount": 24,
  "walls": [
    {
      "elementId": 6799522,
      "id": 6799522,
      "uniqueId": "f8f6455a-...",
      "levelId": 200001,
      "typeId": 310001,
      "typeName": "EX-200mm Concrete",
      "exteriorAreaM2": 42.37
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
- `msg` (string|null): Optional message. When no walls match the filters, a message like `"条件に該当する壁がありません。"` may be returned.
- `totalCount` (int): Number of walls returned in `walls`.
- `walls` (array): Candidate exterior walls (geometry-based candidates).
  - `elementId` / `id`: Wall `ElementId` (int).
  - `uniqueId`: Revit `UniqueId` string.
  - `levelId`: Level `ElementId` (int) associated with the wall (0 if unknown).
  - `typeId`: Wall type `ElementId` (int).
  - `typeName`: Wall type name, if available.
  - `exteriorAreaM2`: Approximate exterior area in m², computed as baseline length × wall height for walls classified as exterior.

## Notes

- This command does not modify the model; it only reads wall baselines, bounding boxes, and Rooms.
- The classification is **purely geometric and Room-based**; it does not rely on wall type names or `ShellLayerType`:
  - It assumes that interior spaces are modeled as Rooms whose vertical extent is reasonably represented by Level/BaseOffset/UpperLimit/LimitOffset.
  - It then looks for walls whose baseline samples separate such Room volumes from non-Room space on only one side.
- `exteriorAreaM2` is an **approximation** derived from baseline length × wall height, not an exact sum over individual faces.
- Because this is a heuristic method, it is best viewed as a **coarse “candidate finder”**:
  - On simple, mostly orthogonal buildings it tends to work well.
  - On highly articulated, deeply recessed, or very irregular envelopes it may misclassify some walls.
  - Walls in internal courtyards, light wells, or around Room-less shafts/spaces may be flagged as exterior depending on how Rooms are modeled.
- For precise envelope definitions or downstream workflows that require strict correctness, you should review the candidates and/or combine this
  result with additional model knowledge or manual checks.
