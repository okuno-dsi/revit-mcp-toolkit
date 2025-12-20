# get_area_geometry

- Category: Area
- Purpose: Get **calculated** Area boundary geometry (segments, mm).

## Overview
This is an **A) calculated boundary** command (see `Manuals/FullManual/spatial_boundary_location.md`).

## Usage
- Method: get_area_geometry

### Parameters
| Name | Type | Required | Default |
|---|---:|:---:|---|
| areaId | int | yes |  |
| boundaryLocation | string | no | `Finish` |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_area_geometry",
  "params": { "areaId": 123456, "boundaryLocation": "CoreCenter" }
}
```

## Related
- get_area_boundary
- create_area
- get_areas
- get_area_params
- update_area
- move_area
- delete_area
- get_area_boundary_walls
Notes:
- Response includes `boundaryLocation` and `geometry` (loops of segments).
