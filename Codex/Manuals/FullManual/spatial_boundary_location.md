# Spatial Boundaries (A/B) and `boundaryLocation`

## When the prompt says “boundary line”, ask which one
In Revit, “boundary line” can mean different things. If the user prompt is ambiguous (e.g. “room boundary line”, “perimeter line”, “trace the boundary”, “use the boundary lines”), **ask which meaning they want before running commands**:

- **A) Calculated boundary (recommended default in most cases)**: `Room.GetBoundarySegments(...)` (computed result; depends on `boundaryLocation`)
- **B) Boundary line elements**: Room/Space/Area *separation/boundary lines* (CurveElements that exist only if someone drew them)

Tip:
- Many rooms have **no Room Separation Lines**, because their boundaries come from walls. In that case, B) may be empty, but A) still works.

## A) Calculated boundaries (computed)
These APIs return **calculated** boundaries based on Revit’s spatial computation:
- `Room/Space/Area.GetBoundarySegments(SpatialElementBoundaryOptions)`
- `Room.IsPointInRoom(...)`, `Space.IsPointInSpace(...)`

In this tool, commands that use these APIs typically accept `boundaryLocation`.

### `boundaryLocation` values
`boundaryLocation` maps to `SpatialElementBoundaryOptions.SpatialElementBoundaryLocation`:
- `Finish` (interior finish / “inside face”)
- `Center` (wall centerline)
- `CoreCenter` (core centerline)
- `CoreBoundary` (core boundary face)

Use-cases (typical):
- `CoreCenter`: planning / structural “core center” workflows
- `Finish`: equipment/finish calculations where “inside clear” matters

## B) Boundary line elements (CurveElements)
These commands operate on **actual line elements** in the model (not computed geometry):
- Room Separation Lines
- Space Separation Lines
- Area Boundary Lines

These are edited via create/move/trim/delete commands for boundary lines.

## Example: “Trace room boundary into area boundary lines”
Use `copy_area_boundaries_from_rooms` and choose `boundaryLocation` depending on your intent:
- Core centerline-based area boundary: `boundaryLocation: "CoreCenter"`
- Inside-clear based area boundary: `boundaryLocation: "Finish"`
