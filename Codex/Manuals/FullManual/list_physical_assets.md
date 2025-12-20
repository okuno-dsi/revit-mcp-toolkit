# list_physical_assets

- Category: ElementOps
- Purpose: List structural (physical) material assets available in the project.

## Overview
This command lists PropertySetElement assets that contain a structural (physical) asset. It is useful for discovering which physical asset names/ids you can assign to materials.

## Usage
- Method: list_physical_assets

### Parameters
| Name        | Type   | Required | Default |
|-------------|--------|----------|---------|
| nameContains | string | no       |         |
| skip        | int    | no       | 0       |
| count       | int    | no       | 2147483647 |

If `_shape.page` is provided (advanced use), `skip` / `count` are taken from there instead.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "list_physical_assets",
  "params": {
    "nameContains": "Concrete",
    "skip": 0,
    "count": 50
  }
}
```

### Example Result (shape)
```json
{
  "ok": true,
  "totalCount": 12,
  "assets": [
    {
      "assetId": 185377,
      "name": "コンクリート",
      "kind": "structural"
    }
  ]
}
```

## Related
- list_thermal_assets
- get_material_assets
- set_material_asset

