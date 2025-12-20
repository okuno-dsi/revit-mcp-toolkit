# delete_detail_lines

- Category: AnnotationOps
- Purpose: Delete multiple Detail Lines (view-specific curves) in one call.

## Usage
- Method: `delete_detail_lines`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementIds | int[] | no / one of |  |
| elementId | int | no / one of |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "delete_detail_lines",
  "params": {
    "elementIds": [123, 456, 789]
  }
}
```

## Related
- delete_detail_line
- get_detail_lines_in_view

