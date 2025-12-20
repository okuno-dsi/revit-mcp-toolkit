# move_space_separation_line

- Category: Space
- Purpose: Move a Space Separation Line by a delta vector (mm input).

## Usage
- Method: `move_space_separation_line`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | int | yes |  |
| dx | number | yes |  |
| dy | number | yes |  |
| dz | number | yes |  |

All deltas are in **mm**.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "move_space_separation_line",
  "params": {
    "elementId": 123456,
    "dx": 100.0,
    "dy": 0.0,
    "dz": 0.0
  }
}
```

## Related
- get_space_separation_lines_in_view

