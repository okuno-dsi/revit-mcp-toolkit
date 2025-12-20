# get_material_assets

- Category: ElementOps
- Purpose: Get structural / thermal assets attached to a material.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It returns which PropertySetElement assets (structural, thermal) are currently assigned to the specified material.

## Usage
- Method: get_material_assets

### Parameters
| Name      | Type   | Required        | Default |
|----------|--------|-----------------|---------|
| materialId | int  | no / one of    | 0       |
| uniqueId | string | no / one of    |         |

At least one of `materialId` or `uniqueId` must be provided.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_material_assets",
  "params": {
    "materialId": 99146
  }
}
```

### Example Result (shape)
```json
{
  "ok": true,
  "materialId": 99146,
  "uniqueId": "...",
  "structural": {
    "assetId": 185377,
    "name": "コンクリート",
    "kind": "structural"
  },
  "thermal": {
    "assetId": 198872,
    "name": "コンクリート",
    "kind": "thermal"
  }
}
```

## Related
- list_physical_assets
- list_thermal_assets
- set_material_asset
- get_material_asset_properties

