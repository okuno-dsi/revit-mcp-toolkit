# view_filter.delete

- Category: ViewFilterOps
- Purpose: Delete a View Filter definition from the project.

## Overview
Deletes a `FilterElement` (parameter or selection filter) from the document.

## Usage
- Method: view_filter.delete

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| filter | object | yes |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view_filter.delete",
  "params": {
    "filter": { "name": "A-WALLS-RC" }
  }
}
```

## Notes
- Deleting a filter can affect many views/templates that use it.

## Related
- view_filter.list
- view_filter.upsert

