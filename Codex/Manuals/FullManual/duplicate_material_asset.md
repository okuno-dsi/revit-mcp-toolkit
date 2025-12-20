# duplicate_material_asset

- Category: ElementOps
- Purpose: Duplicate the current Structural/Thermal asset and re-bind it to the material.

## Overview
Some library assets are effectively read-only. A common workflow is:
1) Duplicate the asset to create an editable PropertySetElement, then
2) Bind the duplicate to the material.

This command automates that workflow.

## Usage
- Method: `duplicate_material_asset`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| materialId | int | no / one of | 0 |
| uniqueId | string | no / one of |  |
| assetKind | string | no | thermal |
| newName | string | no | (auto) |

- `assetKind`: `"thermal"` (default) or `"structural"`.
- If `newName` is omitted, a unique `*_Copy[_n]` name is generated.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "duplicate_material_asset",
  "params": {
    "materialId": 7255081,
    "assetKind": "thermal"
  }
}
```

## Related
- set_material_asset
- set_material_asset_name
- get_material_assets
- get_material_asset_properties

