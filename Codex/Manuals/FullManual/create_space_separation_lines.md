# create_space_separation_lines

- Category: Space
- Purpose: Create Space Separation Lines in a view from supplied curve segments.

## Overview
Creates Space Separation Lines (`CurveElementType.SpaceSeparation`) using `Document.Create.NewSpaceBoundaryLines`.

## Usage
- Method: `create_space_separation_lines`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| viewId | int | no | (active view) |
| segments | object[] | yes |  |

`segments[]` items support:
- Line:
  - `{ "kind": "line", "start": {x,y,z}, "end": {x,y,z} }`
- Arc:
  - `{ "kind": "arc", "center": {x,y,z}, "radiusMm": number, "startAngleDeg": number, "endAngleDeg": number }`

All coordinates are in **mm**.

### Example Request (line polyline)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_space_separation_lines",
  "params": {
    "viewId": 123456,
    "segments": [
      { "kind": "line", "start": { "x": 0, "y": 0, "z": 0 }, "end": { "x": 5000, "y": 0, "z": 0 } },
      { "kind": "line", "start": { "x": 5000, "y": 0, "z": 0 }, "end": { "x": 5000, "y": 4000, "z": 0 } }
    ]
  }
}
```

## Related
- get_space_separation_lines_in_view
- move_space_separation_line
- delete_space_separation_lines

