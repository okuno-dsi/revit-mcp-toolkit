# set_project_base_point

- Category: SiteOps
- Purpose: Set Project Base Point in Revit.

## Overview
Moves the Project Base Point without moving model geometry (sheet/view appearance stays the same, coordinates change). Use this to resolve origin mismatches between architectural/structural models while keeping views unchanged.

## Usage
- Method: set_project_base_point

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| xyzMm | object {x,y,z} | no |  |
| deltaMm | object {x,y,z} | no |  |
| fromPointMm | object {x,y,z} | no |  |
| toPointMm | object {x,y,z} | no |  |
| angleToTrueNorthDeg | number | no | 0.0 |
| sharedSiteName | string | no |  |

**Coordinate rules**
- `xyzMm` sets the absolute base point coordinates (mm).
- `deltaMm` moves the base point by the given offset (mm).
- `fromPointMm` + `toPointMm` will compute `deltaMm = to - from` and apply it.
- If none of `xyzMm/deltaMm/fromPointMm/toPointMm` are provided, only `angleToTrueNorthDeg` (if any) is applied.

### Example Request (absolute)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_project_base_point",
  "params": {
    "xyzMm": {"x": 1000, "y": -500, "z": 0},
    "angleToTrueNorthDeg": 0.0
  }
}
```

### Example Request (delta)
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "set_project_base_point",
  "params": {
    "deltaMm": {"x": -250, "y": 0, "z": 0}
  }
}
```

### Example Request (from/to)
```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "set_project_base_point",
  "params": {
    "fromPointMm": {"x": 10000, "y": 20000, "z": 0},
    "toPointMm": {"x": 0, "y": 0, "z": 0}
  }
}
```

## Related
- set_survey_point
- set_shared_coordinates_from_points
- get_site_overview
