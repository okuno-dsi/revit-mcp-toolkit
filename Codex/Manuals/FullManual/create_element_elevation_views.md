# create_element_elevation_views

- Category: ViewOps
- Purpose: Create elevation-style views focused on arbitrary elements, using either ElevationMarker or SectionBox mode, with robust orientation and optional isolation.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It takes one or more `elementIds`, resolves an appropriate view direction, and creates elevation/section views that look at each element. By default, the resulting views are isolated so that only the target elements are visible.

## Usage
- Method: create_element_elevation_views

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementIds | int[] | yes |  |
| mode | string | no | SectionBox |
| orientation.mode | string | no | Front |
| orientation.customDirection.x/y/z | number | no |  |
| isolateTargets | bool | no | true |
| viewTemplateName | string | no |  |
| viewFamilyTypeName | string | no | MCP_Elevation (fallback to Elevation/Section) |
| viewScale | int | no | 50 |
| cropMargin_mm | number | no | 200 |
| offsetDistance_mm | number | no | 1500 |

Notes:
- `mode`:
  - `"SectionBox"` (default): first attempts `ViewSection.CreateSection` with a custom `BoundingBoxXYZ`; if that fails, it falls back to the ElevationMarker-based implementation.
  - `"ElevationMarker"`: always uses `ElevationMarker.CreateElevation` (door/window-specific cropping is applied here).
- `orientation.mode`:
  - `"Front"` (default), `"Back"`, `"Left"`, `"Right"`, `"CustomVector"`.
  - `"CustomVector"` requires `orientation.customDirection` (x,y,z) to be a non-zero vector.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_element_elevation_views",
  "params": {
    "elementIds": [60576259, 60580001],
    "mode": "ElevationMarker",
    "orientation": {
      "mode": "Front"
    },
    "isolateTargets": true,
    "viewFamilyTypeName": "MCP_Elevation",
    "viewScale": 50,
    "cropMargin_mm": 200,
    "offsetDistance_mm": 1500
  }
}
```

### Example Result
```jsonc
{
  "ok": true,
  "msg": "Created elevation views for 2 element(s).",
  "views": [
    {
      "elementId": 60576259,
      "viewId": 61000001,
      "viewName": "Element_60576259_Elev",
      "mode": "ElevationMarker"
    },
    {
      "elementId": 60580001,
      "viewId": 61000002,
      "viewName": "Element_60580001_Elev",
      "mode": "ElevationMarker"
    }
  ],
  "skippedElements": []
}
```

If some elements cannot be processed (e.g., no valid orientation and no custom vector), they are listed in `skippedElements`:

```jsonc
{
  "ok": true,
  "msg": "Created elevation views for 1 element(s).",
  "views": [ /* ... */ ],
  "skippedElements": [
    {
      "elementId": 60590001,
      "reason": "有効な向きベクトルを決定できませんでした。"
    }
  ]
}
```

## Notes
- When `viewFamilyTypeName` does not match any `ViewFamilyType` by name, the command falls back to a default Elevation (for `mode=ElevationMarker`) or Section (for `mode=SectionBox`) type.
- View naming:
  - When `modeUsed` is `"ElevationMarker"`, the view name is generated as `Element_<elementId>_Elev` (unique name auto-adjustment applies).
  - When `modeUsed` is `"SectionBox"`, the view name is generated as `Element_<elementId>_Sec` (unique name auto-adjustment applies).
- If `orientation.mode` is not `CustomVector` and the element has no usable FacingOrientation/HandOrientation (or for non-instance categories), a global +Y direction is used as a fallback and the reason is recorded in `skippedElements` when orientation could not be determined at all.
- `orientation.doorViewSide` (optional, `"Exterior"` / `"Interior"`) controls whether doors are viewed from the exterior or interior side. The add-in samples points on both sides of the host wall to determine interior/exterior and flips the view direction accordingly (default is `"Exterior"`).
- `depthMode` (optional, `"Auto"` / `"DoorWidthPlusMargin"`) controls how far the view extends in depth. `"DoorWidthPlusMargin"` uses the element thickness plus a margin for doors/windows/walls/curtain panels.
- For doors/windows (and curtain wall panels), the crop box in the view plane is computed from the type width/height (when available) and the element’s geometry center so that the element is centered with symmetric margins. Other elements use the projected BoundingBox with an added margin.
- When `isolateTargets` is true, all visible elements in the new view except the specified `elementIds` are hidden via `HideElements`, so the view persistently shows only the target elements.

## Related
- create_section
- create_elevation_view
- get_oriented_bbox
