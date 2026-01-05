# get_spatial_context_for_elements

- Category: Spatial
- Purpose: For multiple elements, sample reference points and curve endpoints/midpoints and report which Rooms / Spaces / Areas they belong to.

## Overview
This command is a multi-element extension of `get_spatial_context_for_element`.
It takes a list of `elementIds`, samples:

- A representative point (Location / bounding-box center)
- For curve-based elements, additional points along the curve (start / mid / end by default)

and returns only those elements where at least one sample point lies inside a Room / Space / Area.

## Usage
- Method: get_spatial_context_for_elements

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementIds | int[] | yes | - |
| phaseName | string | no |  |
| mode | string | no | `"3d"` |
| include | string[] | no | (all) |
| curveSamples | number[] | no | `[0.0, 0.5, 1.0]` |
| maxElements | int | no | `int.MaxValue` |
| bboxFootprintProbe | bool | no | `true` |

- `elementIds`  
  - Array of `ElementId.IntegerValue` for the elements to analyse (walls, floors, tags, etc.).  
  - To target “all categories with a valid Location”, first collect IDs using other commands (e.g. `get_elements_in_view`, category-specific queries) and then pass them here.
- `phaseName` / `mode` / `include`  
  - Same semantics as `get_spatial_context_for_element`.  
  - If `include` is omitted, all of `"room"`, `"space"`, `"zone"`, `"area"`, `"areaScheme"` are included.
- `curveSamples`  
  - Normalised curve parameters `t` in `[0.0, 1.0]` used when sampling `LocationCurve` elements.  
  - Default `[0.0, 0.5, 1.0]` = start / mid / end.
- `maxElements`  
  - Optional safety limit on how many elements to process in one call.
- `bboxFootprintProbe`
  - If `true` (default), Room resolution may probe the element bbox footprint at mid-height when a sample point does not hit a Room.
  - Set `false` to disable bbox footprint probing.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_spatial_context_for_elements",
  "params": {
    "elementIds": [60500001, 60500002, 60500003],
    "phaseName": "New Construction",
    "mode": "3d",
    "include": ["room", "space", "area"],
    "curveSamples": [0.0, 0.25, 0.5, 0.75, 1.0]
  }
}
```

## Response (success)

```jsonc
{
  "ok": true,
  "totalCount": 2,
  "elements": [
    {
      "elementId": 60500001,
      "category": "Walls",
      "samples": [
        {
          "kind": "reference",
          "point": { "x": 10000.0, "y": 20000.0, "z": 0.0 },
          "room": { "id": 60535031, "name": "Storage A 32", "number": "32", "phase": "", "levelName": "1FL" },
          "spaces": [],
          "areas": []
        },
        {
          "kind": "curveStart",
          "point": { "x": 9500.0, "y": 20000.0, "z": 0.0 },
          "room": null,
          "spaces": [
            { "id": 60576295, "name": "Space 7", "number": "7", "levelName": "1FL", "zone": null }
          ],
          "areas": []
        }
      ]
    },
    {
      "elementId": 60500002,
      "category": "Windows",
      "samples": [
        {
          "kind": "location",
          "point": { "x": 12000.0, "y": 21000.0, "z": 900.0 },
          "room": { "id": 60535025, "name": "Office A 30", "number": "30", "phase": "", "levelName": "1FL" },
          "spaces": [],
          "areas": [
            {
              "id": 60576279,
              "name": "Area 7",
              "number": "7",
              "areaScheme": { "id": 9490, "name": "07 Building Gross" }
            }
          ]
        }
      ]
    }
  ],
  "messages": [
    "Room was resolved using the final project phase.",
    "Found 1 Space(s) at the reference point.",
    "No Area on the same level contained the reference point (bounding-box based approximation)."
  ]
}
```

### Behaviour Notes
- For each element:
  - Samples include a `reference` point, and for curves, `curveStart` / `curveMid` / `curveEnd` / `curveParam` according to `curveSamples`.
  - If no sample point hits any Room / Space / Area, that element is omitted from the `elements` list.
- Room resolution is best-effort and may use element bbox probes (mid-height + bbox footprint) when the representative point does not hit a Room.
- `messages` aggregates helpful diagnostics from Room / Space / Area resolution (phase used, counts, approximations, etc.).

## Related
- get_spatial_context_for_element  
- classify_points_in_room  
- get_spaces  
- get_areas  
