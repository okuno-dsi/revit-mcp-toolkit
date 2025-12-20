# list_thermal_assets

- Category: ElementOps
- Purpose: List thermal material assets available in the project.

## Overview
This command lists PropertySetElement assets that contain a thermal asset. It is useful for discovering which thermal asset names/ids you can assign to materials.

## Usage
- Method: list_thermal_assets

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
  "method": "list_thermal_assets",
  "params": {
    "nameContains": "コンクリート",
    "skip": 0,
    "count": 50
  }
}
```

### Example Result (shape)
```json
{
  "ok": true,
  "totalCount": 3,
  "assets": [
    {
      "assetId": 198872,
      "name": "コンクリート",
      "kind": "thermal"
    }
  ]
}
```

## Related
- list_physical_assets
- get_material_assets
- set_material_asset

