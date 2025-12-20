# get_material_asset_properties

- Category: ElementOps
- Purpose: Read structural / thermal asset properties (name, unit, value, id) attached to a material.

## Overview
This command inspects the StructuralAsset and ThermalAsset referenced by a material and returns their public properties in a generic shape. It is intended for analysis and documentation rather than round-trip editing.

## Usage
- Method: get_material_asset_properties

### Parameters
| Name       | Type   | Required | Default |
|------------|--------|----------|---------|
| materialId | int    | no / one of | 0     |
| uniqueId   | string | no / one of |       |
| assetKind  | string | no       | ""      |

- `materialId` or `uniqueId` must be provided.
- `assetKind`:
  - `""` (empty / omitted): return both structural and thermal assets (if present).
  - `"structural"`: structural asset only.
  - `"thermal"`: thermal asset only.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_material_asset_properties",
  "params": {
    "materialId": 99146,
    "assetKind": "thermal"
  }
}
```

### Example Result (shape)
```json
{
  "ok": true,
  "materialId": 99146,
  "uniqueId": "...",
  "thermal": {
    "assetId": 198872,
    "name": "コンクリート",
    "kind": "thermal",
    "properties": [
      {
        "id": "ThermalConductivity",
        "name": "ThermalConductivity",
        "storageType": "Double",
        "unit": "W/(m·K)",
        "value": 0.025
      },
      {
        "id": "Density",
        "name": "Density",
        "storageType": "Double",
        "unit": "kg/m3",
        "value": 2400.0
      }
    ]
  }
}
```

## Related
- get_material_assets
- list_physical_assets
- list_thermal_assets
- set_material_thermal_conductivity

