# get_space_separation_lines_in_view

- Category: Space
- Purpose: List Space Separation Lines in a view (returns geometry in mm).

## Usage
- Method: `get_space_separation_lines_in_view`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| viewId | int | no | (active view) |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_space_separation_lines_in_view",
  "params": { "viewId": 123456 }
}
```

## Related
- create_space_separation_lines
- move_space_separation_line
- delete_space_separation_lines

