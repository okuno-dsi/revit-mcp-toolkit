# delete_detail_lines

- Category: AnnotationOps
- Purpose: (Deprecated) Use `view.delete_detail_line` with `elementIds[]` instead.

## Status
- This command is deprecated / removed in recent builds.
- Replacement: `view.delete_detail_line` (bulk supported via `elementIds[]`).

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
