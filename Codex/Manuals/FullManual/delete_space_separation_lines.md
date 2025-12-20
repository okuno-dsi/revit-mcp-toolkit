# delete_space_separation_lines

- Category: Space
- Purpose: Delete Space Separation Lines by ids.

## Usage
- Method: `delete_space_separation_lines`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementIds | int[] | yes |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "delete_space_separation_lines",
  "params": {
    "elementIds": [111, 222, 333]
  }
}
```

## Related
- get_space_separation_lines_in_view
- create_space_separation_lines

