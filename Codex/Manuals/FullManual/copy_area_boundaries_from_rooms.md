# copy_area_boundaries_from_rooms

- Category: Area
- Purpose: Copy Area Boundaries From Rooms in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: copy_area_boundaries_from_rooms

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| batchSize | int | no/depends | 50 |
| boundaryLocation | string | no | `Finish` |
| maxMillisPerTx | int | no/depends | 200 |
| mergeToleranceMm | number | no/depends | 3.0 |
| refreshView | bool | no/depends | false |
| startIndex | int | no/depends | 0 |
| boundaryCurveSource | string | no | `BoundarySegment` |
| preferLineElements | bool | no | (overrides `boundaryCurveSource` if set) |

`boundaryLocation` controls `SpatialElementBoundaryOptions.SpatialElementBoundaryLocation` when calling `Room.GetBoundarySegments(...)`:
- `Finish` (inner finish face / 内法)
- `Center` (wall centerline)
- `CoreCenter` (wall core centerline / 躯体芯)
- `CoreBoundary` (wall core boundary face)

Notes:
- This command primarily copies **A)** “computed Room boundaries” into **Area Boundary Lines**.
- If `boundaryCurveSource=PreferLineElements`, any boundary segment that references a `CurveElement` (e.g. *Room Separation Line*) prefers the **line element geometry**, but endpoints are clipped to the `BoundarySegment` endpoints to keep the loop closed (hybrid mode).
- If you need **pure B)** (copy only the line elements as-is, even if the loop is not closed), use `get_room_boundary_lines_in_view` + `create_area_boundary_line` (or write a script that copies CurveElements).

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "copy_area_boundaries_from_rooms",
  "params": {
    "batchSize": 0,
    "boundaryLocation": "CoreCenter",
    "maxMillisPerTx": 0,
    "mergeToleranceMm": 0.0,
    "refreshView": false,
    "startIndex": 0
  }
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

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "mergeToleranceMm": {
      "type": "number"
    },
    "refreshView": {
      "type": "boolean"
    },
    "startIndex": {
      "type": "integer"
    },
    "batchSize": {
      "type": "integer"
    },
    "maxMillisPerTx": {
      "type": "integer"
    }
  }
}
```

### Result Schema
```json
{
  "type": "object",
  "properties": {},
  "additionalProperties": true
}
```
