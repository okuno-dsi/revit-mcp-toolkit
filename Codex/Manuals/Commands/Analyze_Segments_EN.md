# Geometry Command: `analyze_segments`

Fast, unit‑safe geometry analysis for segments and points. Supports 2D (XY) and full 3D. Returns rich relations (parallel/colinear/intersection/closest points) and point‑to‑segment projections with distances in millimeters.

- Category: Geometry
- Kind: read
- Units: inputs and outputs in millimeters (angles in degrees). Internally Revit uses ft/rad but the command normalizes to SI via UnitHelper.

## What It Does
- Segment vs Segment
  - 2D (XY projection) or 3D line‑line analysis
  - Parallel/colinear checks, intersection, shortest distance, closest points, overlap (2D)
- Point vs Segment
  - Distance from point to segment
  - Projection point, parameter `t` (0..1), inside/outside flag `onSegment`

## Inputs (JSON)
- `mode`: "2d" | "3d" (default: "2d")
- `seg1`: `{ a:{x,y(,z)}, b:{x,y(,z)} }` — first segment endpoints in mm
- `seg2`: `{ a:{x,y(,z)}, b:{x,y(,z)} }` — second segment endpoints in mm
- `point?`: `{ x,y(,z) }` — optional point to analyze against both segments
- `tol?`: `{ distMm?: number, angleDeg?: number }` — numeric tolerances
  - Typical values: `distMm: 0.1 ~ 5.0`, `angleDeg: 0.1` (for practical parallel tests)

Notes:
- In 2D mode, Z is ignored and XY projection is used.
- In 3D mode, full XYZ is considered.

## Result (shape)
- `mode`: echo of input
- `line` (segment vs segment):
  - 2D: `{ isParallel, isColinear, angleDeg, intersectionExists, intersection?, distanceBetweenParallelMm?, overlapExists, overlapLengthMm?, overlapStart?, overlapEnd? }`
  - 3D: `{ isParallel, isColinear, angleDeg, intersects, intersection?, shortestDistanceMm, closestOn1{ x,y,z }, closestOn2{ x,y,z } }`
- `pointToSeg1` and `pointToSeg2` (when `point` is provided):
  - `{ ok, msg, distanceMm, projection{ x,y(,z) }, t, onSegment }`

`onSegment` is true when the orthogonal projection lies within the segment extent (with distance tolerance). `t` is the parametric coordinate: 0 at `a`, 1 at `b`.

## Examples

### 1) 3D point → segment distance
```bash
python -X utf8 Scripts/Reference/send_revit_command_durable.py \
  --port 5210 \
  --command analyze_segments \
  --params '{
    "mode":"3d",
    "seg1": { "a": {"x":0,   "y":0,   "z":0},
               "b": {"x":1000,"y":0,   "z":0} },
    "seg2": { "a": {"x":0,   "y":0,   "z":0},
               "b": {"x":1000,"y":0,   "z":0} },
    "point": { "x":250, "y":12.3, "z":0 },
    "tol": { "distMm": 0.1, "angleDeg": 1e-4 }
  }'
```
Key outputs: `pointToSeg1.distanceMm`, `pointToSeg1.projection`, `pointToSeg1.onSegment`.

### 2) 2D segment relations (overlap/intersection)
```bash
python -X utf8 Scripts/Reference/send_revit_command_durable.py \
  --port 5210 \
  --command analyze_segments \
  --params '{
    "mode":"2d",
    "seg1": { "a": {"x":0, "y":0},   "b": {"x":100, "y":0} },
    "seg2": { "a": {"x":50,"y":0},   "b": {"x":150, "y":0} },
    "tol": { "distMm": 0.1, "angleDeg": 0.1 }
  }'
```
Check: `line.overlapExists`, `line.overlapLengthMm`, `line.intersectionExists`.

### 3) With Structural Frames or Walls
- Get endpoints (mm) for a frame: `get_structural_frames { elementId }` → `structuralFrames[0].start/end`.
- Or a wall baseline: `get_wall_baseline { elementId }` → `baseline.start/end` (or `baseline.points[0/-1]`).
- Then call `analyze_segments` with `mode:"3d"` and `point` from another element endpoint.

Example (point on selected element’s baseline):
```json
{
  "mode": "3d",
  "seg1": { "a": {"x": ax, "y": ay, "z": az}, "b": {"x": bx, "y": by, "z": bz} },
  "seg2": { "a": {"x": ax, "y": ay, "z": az}, "b": {"x": bx, "y": by, "z": bz} },
  "point": { "x": px, "y": py, "z": pz },
  "tol": { "distMm": 0.1, "angleDeg": 1e-4 }
}
```
Interpretation:
- If `pointToSeg1.onSegment == true` and `distanceMm` ~ 0, the point lies on the baseline within tolerance.

## Tips & Pitfalls
- Keep coordinates in millimeters. Use `get_structural_frames` and `get_wall_baseline` which already return mm.
- Choose tolerances for your precision needs:
  - `distMm`: 0.1–5.0 mm is typical
  - `angleDeg`: small value for parallel test (e.g., 0.1)
- For 2D checks, z isn’t considered. Use 3D where vertical alignment matters.
- Very short segments: when `|b-a|` is nearly zero, distance degenerates to point‑point.

## Minimal Client Snippet (Python)
```python
import json, subprocess, sys
payload = {
  "mode":"3d",
  "seg1": {"a": {"x":0,"y":0,"z":0}, "b": {"x":1000,"y":0,"z":0}},
  "seg2": {"a": {"x":0,"y":0,"z":0}, "b": {"x":1000,"y":0,"z":0}},
  "point": {"x":250,"y":12.3,"z":0},
  "tol": {"distMm":0.1,"angleDeg":1e-4}
}
args = [sys.executable, 'Scripts/Reference/send_revit_command_durable.py', '--port','5210',
        '--command','analyze_segments','--params', json.dumps(payload), '--force']
print(subprocess.run(args, capture_output=True, text=True).stdout)
```

## See Also
- `get_structural_frames` — frame endpoints (mm) and metadata
- `get_wall_baseline` — wall LocationCurve in mm
- `get_element_info` — quick way to get bboxMm/locations




