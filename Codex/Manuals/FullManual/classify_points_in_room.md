# classify_points_in_room

- Category: Spatial
- Purpose: For a given Room / Space, classify multiple points (X,Y,Z) as inside or outside.

## Overview
This command is a **point-only** classifier: it takes raw points and tests whether each point lies inside a specified Room or MEP Space.

- It does **not** consider the vertical extent (height) of elements such as columns or walls.
- When all points fall outside, the response includes a `messages` hint suggesting use of element-based commands (`get_spatial_context_for_element` / `get_spatial_context_for_elements`) for deeper analysis.

## Usage
- Method: classify_points_in_room

### Parameters
| Name   | Type        | Required | Description                                     |
|--------|-------------|----------|-------------------------------------------------|
| roomId | int/string  | yes      | `ElementId` of the Room or Space to test       |
| points | number[][]  | yes      | Array of `[x_mm, y_mm, z_mm]` coordinates (mm) |

- `roomId`  
  - `ElementId.IntegerValue` or its string representation.  
  - Must refer to an `Autodesk.Revit.DB.Architecture.Room` or `Autodesk.Revit.DB.Mechanical.Space`.
- `points`  
  - Coordinates in millimeters, e.g. `[[1000.0, 2000.0, 0.0], [1500.0, 2500.0, 0.0]]`.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "classify_points_in_room",
  "params": {
    "roomId": 6809314,
    "points": [
      [21961.201, 5686.485, 12300.0],
      [22000.0, 5700.0, 12300.0]
    ]
  }
}
```

## Response

```jsonc
{
  "ok": true,
  "inside": [
    [21961.201, 5686.485, 12300.0]
  ],
  "outside": [
    [22000.0, 5700.0, 12300.0]
  ],
  "messages": [
    "All points were classified as outside the Room/Space. If you need to know whether elements such as columns or walls pass through this room, consider using element-based commands like get_spatial_context_for_element / get_spatial_context_for_elements."
  ]
}
```

- `inside`  
  - Points classified as inside the Room/Space (same format as input: `[x_mm,y_mm,z_mm]`).
- `outside`  
  - Points classified as outside.
- `messages`  
  - Additional hints.  
  - When `inside` is empty, a hint is included to try element-based commands for vertical extent checks.

## Difference from get_spatial_context_for_element(s)

- `classify_points_in_room`  
  - Input: `roomId` (Room/Space) and `points`.  
  - Output: Whether each point is inside that Room/Space.  
  - **Does not consider element vertical extents or bounding boxes.**

- `get_spatial_context_for_element` / `get_spatial_context_for_elements`  
  - Input: element IDs (`elementId` / `elementIds`).  
  - Output: For each element and sample point, the Room / Space / Area context.  
  - With the recent enhancement, these commands probe using the element’s bounding box and test at a **mid-height Z** as well as the base point, so they can detect cases such as:
    - “The column base is 100 mm below the level, but the column passes through the room at mid-height.”

### Recommended Usage

- “Is this specific point inside the room?” → `classify_points_in_room` (simple and fast).  
- “Do columns or walls pass through the room level, even if their base points are below or above?” →  
  - First, do a quick point test with `classify_points_in_room`.  
  - If all points are outside, follow up with `get_spatial_context_for_element` / `get_spatial_context_for_elements` for element-based, height-aware analysis.

