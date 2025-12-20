# get_area_boundary

- Category: Area
- Purpose: Get **calculated** Area boundary loops (`GetBoundarySegments`) as mm coordinates.

## Overview
This is an **A) calculated boundary** command (see `Manuals/FullManual/spatial_boundary_location.md`).
If you need to edit actual Area Boundary Lines (CurveElements), use the `*_area_boundary_line` commands.

## Usage
- Method: get_area_boundary

### Parameters
| Name | Type | Required | Default |
|---|---:|:---:|---|
| areaId | int | yes |  |
| boundaryLocation | string | no | `Finish` |

`boundaryLocation` maps to `SpatialElementBoundaryOptions.SpatialElementBoundaryLocation`:
`Finish` / `Center` / `CoreCenter` / `CoreBoundary`.

### Example Request (CoreCenter)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_area_boundary",
  "params": { "areaId": 123456, "boundaryLocation": "CoreCenter" }
}
```

## Related
- create_area
- get_areas
- get_area_params
- update_area
- move_area
- delete_area
- get_area_boundary_walls
- get_area_centroid
- get_area_boundary_lines_in_view
- create_area_boundary_line
- delete_area_boundary_line
- move_area_boundary_line
- trim_area_boundary_line
- extend_area_boundary_line

Notes:
- Response includes `loops[].points` (start points) and `loops[].segments` (start/end).
