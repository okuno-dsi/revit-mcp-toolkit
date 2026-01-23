# create_room_boundary_line

- Category: Room
- Purpose: Deprecated alias of `room_separation.create_lines` (Room Separation Lines).

## Overview
This method name remains callable for backward compatibility, but new code should use:

- `room_separation.create_lines` (canonical)

## Usage
- Method: `create_room_boundary_line` (deprecated alias)
- Canonical: `room_separation.create_lines`
- Transaction: Write

### Parameters
Same as `room_separation.create_lines`:

| Name | Type | Required | Default |
|---|---|---:|---|
| viewId | int | no | ActiveView |
| unit | string | no | `"mm"` |
| snapZToViewLevel | bool | no | `true` |
| segments | object[] | yes (recommended) |  |
| start | object | legacy |  |
| end | object | legacy |  |

### Example Request (segments)
```json
{
  "jsonrpc": "2.0",
  "id": "rs-legacy-1",
  "method": "create_room_boundary_line",
  "params": {
    "viewId": 123456,
    "unit": "mm",
    "segments": [
      { "start": { "x": 0, "y": 0, "z": 0 }, "end": { "x": 5000, "y": 0, "z": 0 } }
    ]
  }
}
```

## Result
- `result.created`: int[] created ids
- `result.count`: int
- `result.skipped[]`: `{ index, reason }`

## Related
- `room_separation.create_lines`
