# Command: get_instances_geometry

Batch triangulated geometry for multiple elements across categories. Useful for fast visualization/analysis pipelines without per‑item round‑trips.

- Category: Other
- Kind: read
- Units: vertices in Revit internal feet (same as `get_instance_geometry`)

## Inputs (JSON)
- `elementIds?`: int[]
- `uniqueIds?`: string[]
- `fromSelection?`: bool (use current selection)
- `detailLevel?`: "Coarse" | "Medium" | "Fine" (default: Fine)
- `includeNonVisible?`: bool (default: false)
- `weld?`: bool (default: true)
- `weldToleranceMm?`: number (mm, optional; if omitted `weldTolerance` in feet is honored)
- `weldTolerance?`: number (feet; default ~1e-6)
- `page?`: `{ startIndex, batchSize }`
- `includeAnalytic?`: bool (default: false)

At least one of `elementIds`, `uniqueIds`, or `fromSelection` must be provided. When paging is used, the server slices the resolved id list.

## Result
```
{
  "ok": true,
  "items": [
    {
      "ok": true,
      "elementId": 12345,
      "uniqueId": "...",
      "category": "Walls",
      "typeName": "...",
      "units": "feet",
      "transform": [[...4x4...]],
      "vertices": [x0,y0,z0, x1,y1,z1, ...],
      "normals": [nx0,ny0,nz0, ...],
      "uvs": [u0,v0, ...],
      "submeshes": [{"materialId": -1, "indices": [0,1,2, ...]}],
      "materials": [{"materialId": -1, "name": ""}],
      "analytic": {
        // present when includeAnalytic=true and a fallback was computed
        // Rooms/Spaces: { kind:"room"|"space", contours:[[{x,y,z},...], ...] }
        // LocationCurve: { kind:"curve", wire:{ a:{x,y,z}, b:{x,y,z} } }
        // LocationPoint: { kind:"point", point:{x,y,z} }
      }
    },
    { "ok": false, "elementId": 999, "error": "not_found" }
  ],
  "nextIndex": null,
  "completed": true,
  "totalCount": 8
}
```

## Examples
- Current selection, medium detail, page of 200
```
python -X utf8 Scripts/Reference/send_revit_command_durable.py \
  --port 5210 --command get_instances_geometry \
  --params '{"fromSelection":true,"detailLevel":"Medium","page":{"startIndex":0,"batchSize":200}}' --force
```

- Explicit ids with coarse detail and 1 mm weld tolerance
```
python -X utf8 Scripts/Reference/send_revit_command_durable.py \
  --port 5210 --command get_instances_geometry \
  --params '{"elementIds":[101,202,303],"detailLevel":"Coarse","weld":true,"weldToleranceMm":1.0}' --force
```

## Notes
- Output shape matches `get_instance_geometry` per element for easy drop‑in.
- Triangulation respects nested GeometryInstance transforms.
- For very large selections, prefer paging to keep payloads manageable.
- `includeNonVisible` allows exporting geometry hidden in the current view.
- `includeAnalytic` adds fallback shape for elements without solids/meshes:
  - Rooms/Spaces → boundary contours (mm)
  - Beams/columns/walls with LocationCurve → wire endpoints (mm)
  - Point‑only elements → single point (mm)

## See Also
- `get_instance_geometry` — single element variant
- `analyze_segments` — geometry relations (2D/3D)



