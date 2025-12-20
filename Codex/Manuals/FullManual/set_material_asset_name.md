# set_material_asset_name

- Category: ElementOps
- Purpose: Rename the Structural/Thermal asset assigned to a material.

## Overview
Attempts to rename the asset in-place; if not editable, duplicates and re-binds an asset with the requested name.

## Usage
- Method: `set_material_asset_name`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| materialId | int | no / one of | 0 |
| uniqueId | string | no / one of |  |
| assetKind | string | yes |  |
| newName | string | yes |  |

- `assetKind`: `"structural"` or `"thermal"`.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_material_asset_name",
  "params": {
    "materialId": 7255081,
    "assetKind": "thermal",
    "newName": "Concrete_ThermalAsset_Test"
  }
}
```

## Related
- set_material_asset
- duplicate_material_asset

