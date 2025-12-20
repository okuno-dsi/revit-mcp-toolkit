# get_roofs

- Category: ElementOps
- Purpose: Query Roof elements with filters + paging.

## Overview
Supports both:
- legacy paging (`skip`/`count`/`namesOnly`), and
- `_shape.page` paging (`limit`/`skip`) plus `idsOnly`.

## Usage
- Method: `get_roofs`

### Parameters (high level)
| Name | Type | Required | Default |
|---|---|---|---|
| skip | int | no | 0 |
| count | int | no | (all) |
| namesOnly | bool | no | false |
| _shape | object | no |  |
| summaryOnly | bool | no | false |
| includeLocation | bool | no | true |
| elementId | int | no | 0 |
| uniqueId | string | no |  |
| typeId / roofTypeId | int | no | 0 |
| typeName | string | no |  |
| familyName | string | no |  |
| levelId | int | no | 0 |
| levelName | string | no |  |
| nameContains | string | no |  |

`_shape`:
```jsonc
{
  "idsOnly": false,
  "page": { "limit": 100, "skip": 0 }
}
```

### Example Request (ids only, first page)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_roofs",
  "params": {
    "_shape": { "idsOnly": true, "page": { "limit": 100, "skip": 0 } }
  }
}
```

## Related
- create_roof
- delete_roof
- move_roof

