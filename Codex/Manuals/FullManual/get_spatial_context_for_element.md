# get_spatial_context_for_element

- Category: Spatial
- Purpose: Get the Room / Space / Zone / Area context for a single element.

## Overview
This command is executed via JSON-RPC against the Revit MCP add-in and returns the *spatial context* for one element.

Given an `elementId`, it reports:
- The representative `referencePoint` (in mm).
- The containing `Room` (if any).
- The containing `Space`(s) and their `Zone` (if any).
- The containing `Area`(s) and their `AreaScheme`(s).

If the target element itself is a Space or Area, that Space / Area is always included in the result even when the reference point does not hit any container by heuristic tests.

## Usage
- Method: get_spatial_context_for_element

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | int | yes | - |
| phaseName | string | no |  |
| mode | string | no | `"3d"` |
| include | string[] | no | (all) |
| bboxFootprintProbe | bool | no | `true` |

- `elementId`  
  - `ElementId.IntegerValue` of the target element (wall, floor, equipment, tag, etc.).  
  - This is required. To use the current Revit selection, first call `get_selected_element_ids` and pass one of those IDs.
- `phaseName`  
  - Optional phase name used for Room / Space queries.  
  - If omitted, the Revit API “final project phase” is used.
- `mode`  
  - `"3d"`: check XY+Z for Room / Space (default). Area is still 2D.  
  - `"2d"`: XY-only checks for debugging / theoretical workflows.
- `include`  
  - Optional list of what to include: any of `"room"`, `"space"`, `"zone"`, `"area"`, `"areaScheme"`.  
  - If omitted, all of the above are included.
- `bboxFootprintProbe`
  - If `true` (default), Room resolution also probes the element bbox footprint at mid-height when the representative point does not hit a Room.
  - Set `false` to disable bbox footprint probing (more strict but can miss boundary-crossing elements).

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_spatial_context_for_element",
  "params": {
    "elementId": 60576295,
    "phaseName": "New Construction",
    "mode": "3d",
    "include": ["room", "space", "zone", "area", "areaScheme"]
  }
}
```

## Response (success)

```jsonc
{
  "ok": true,
  "elementId": 60576295,
  "referencePoint": {
    "x": 5038.119,
    "y": 20415.052,
    "z": 0.0
  },
  "room": {
    "id": 60535031,
    "name": "Storage A 32",
    "number": "32",
    "phase": "",
    "levelName": "1FL"
  },
  "spaces": [
    {
      "id": 60576295,
      "name": "Space 7",
      "number": "7",
      "phase": "",
      "levelName": "1FL",
      "zone": {
        "id": 635618,
        "name": "Default"
      }
    }
  ],
  "areas": [
    {
      "id": 60576279,
      "name": "Area 7",
      "number": "7",
      "areaScheme": {
        "id": 9490,
        "name": "07 Building Gross"
      }
    }
  ],
  "areaSchemes": [
    { "id": 9490, "name": "07 Building Gross" }
  ],
  "messages": [
    "Room was resolved using the final project phase.",
    "Found 1 Space(s) at the reference point.",
    "No Area on the same level contained the reference point (bounding-box based approximation).",
    "Area resolution: reference point did not hit any Area, but the element itself is an Area, so it was included."
  ]
}
```

### Behaviour Notes
- `referencePoint`  
  - Computed from the element’s LocationPoint, LocationCurve midpoint, or bounding-box center (internal ft → mm).  
- `room`  
  - Best-effort Room resolution:
    - Tries the reference point, and also probes at the element bbox mid-height.
    - If the representative XY is outside but the element bbox footprint crosses a Room boundary, it also probes bbox corners/midpoints at mid-height to find a containing Room.
- `spaces`  
  - All Spaces containing the reference point.  
  - If none are found but the element itself is a `Mechanical.Space`, the command adds that Space as a best-effort context.
- `areas` / `areaSchemes`  
  - Areas on the same Level whose *bounding box* contains the reference point’s XY are treated as containing Areas (approximate).  
  - If none are found but the element itself is an `Area`, the command adds that Area and its AreaScheme.
- `messages`  
  - Human-readable diagnostics and caveats about phase resolution, counts, and heuristics.

## Related
- get_rooms  
- get_spaces  
- get_areas  
- map_room_area_space
*** End Patch ***!
