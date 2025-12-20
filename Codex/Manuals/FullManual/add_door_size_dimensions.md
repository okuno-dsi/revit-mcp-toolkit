# add_door_size_dimensions

- Category: AnnotationOps
- Purpose: Add associative door width/height dimensions in a view (typically an elevation/section).

## Overview
Creates Revit `Dimension` elements for door **Width** and/or **Height** so the door size is readable in the view.

- The command targets door `FamilyInstance` elements.
- It uses `FamilyInstanceReferenceType` references (`Left/Right` for width, `Bottom/Top` for height) so the dimension is **associative** (updates with the door).
- The dimension line is placed using the door bounding box projected into the view coordinate system, with an offset (mm).
- Supports stacked/multi-offset dimensions and optional custom reference-pair dimensions.

## Usage
- Method: `add_door_size_dimensions`

### Parameters
```jsonc
{
  "viewId": 0,                    // optional (default: active view)
  "doorIds": [123],               // optional (alias: elementIds). If omitted, uses all doors visible in the view.
  "addWidth": true,               // optional (default: true)
  "addHeight": true,              // optional (default: true)

  // Offsets (mm). Arrays => create multiple stacked dimensions.
  "offsetMm": 200.0,              // optional (default: 200) fallback offset
  "widthOffsetMm": 200.0,         // optional single width offset
  "heightOffsetMm": 200.0,        // optional single height offset
  "widthOffsetsMm": [150, 300],   // optional stacked width offsets (takes precedence)
  "heightOffsetsMm": [150, 300],  // optional stacked height offsets (takes precedence)

  // Offset behavior
  // - absolute  : offsetMm is a model-space distance from the element bounds (legacy)
  // - leaderPlus: finalOffsetMm = (leaderLengthPaperMm * view.Scale) + offsetMm
  "offsetMode": "leaderPlus",      // optional: "leaderPlus"|"absolute" (default: "leaderPlus")
  "leaderLengthPaperMm": null,     // optional override (paper mm) for leaderPlus base offset

  // Placement
  "widthSide": "top",             // optional: "top"|"bottom" (default: "top")
  "heightSide": "left",           // optional: "left"|"right" (default: "left")

  // Dimension type (optional). You can specify either typeId or typeName.
  "typeId": 0,                    // optional DimensionType id (0 = keep default)
  "typeName": null,               // optional DimensionType name (case-insensitive exact match)

  // View template / visibility safety
  "detachViewTemplate": false,    // optional (default: false)
  "ensureDimensionsVisible": true,// optional (default: true) unhide Dimensions category if hidden
  "keepInsideCrop": true,         // optional (default: true) clamp dimension line into CropBox extents
  "expandCropToFit": true,        // optional (default: same as keepInsideCrop) expand CropBox instead of clamping
  "cropMarginMm": 30.0,           // optional (default: 30) clamp margin

  // Optional override for created dimensions (projection line color)
  "overrideRgb": { "r": 255, "g": 0, "b": 0 },

  // Advanced: custom dimensions (arbitrary reference pairs)
  // refA/refB support:
  //  - stable string: "...."
  //  - object: { "stable":"...." } or { "refType":"Left", "index":0 }
  "dimensionSpecs": [
    {
      "enabled": true,
      "name": "custom_1",
      "orientation": "horizontal", // "horizontal"|"vertical"
      "side": "top",               // horizontal: "top"|"bottom" / vertical: "left"|"right"
      "offsetMm": 400,
      "refA": "STABLE_REF_A",
      "refB": "STABLE_REF_B",
      "typeId": 0,
      "typeName": null
    }
  ],

  "debug": false                  // optional (default: false) include bbox debug info
}
```

### Example Request (active view)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "add_door_size_dimensions",
  "params": {
    "offsetMm": 200,
    "addWidth": true,
    "addHeight": true
  }
}
```

### Example Result
```jsonc
{
  "ok": true,
  "viewId": 11123842,
  "viewName": "DoorType ...",
  "doorCount": 1,
  "createdCount": 1,
  "items": [
    {
      "elementId": 7282928,
      "created": [
        { "kind": "width", "dimensionId": 12345678 },
        { "kind": "height", "dimensionId": 12345679 }
      ]
    }
  ],
  "skipped": []
}
```

## Notes
- If the door family does not expose the required reference planes (`Left/Right` and/or `Bottom/Top`), the corresponding dimension is skipped and reported in `skipped`.
- If `ensureDimensionsVisible` needs to change visibility but the view template is locked, the command returns `ok:false` with `code:"VIEW_TEMPLATE_LOCK"` (use `detachViewTemplate:true` or detach manually).
- In `offsetMode:"leaderPlus"` (default), the offset is automatically increased by a **paper-space** base length (resolved from the DimensionType when possible), scaled by `view.Scale`, so dimensions land outside tight element-view crops more predictably.
- For internal sub-feature dimensions (glass/louver/knob, etc.), first discover stable references via `get_family_instance_references`, then pass them via `dimensionSpecs`.
