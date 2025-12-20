# set_material_thermal_conductivity

- Category: ElementOps
- Purpose: Set the thermal conductivity (λ) of a material's thermal asset.

## Overview
This command updates the `ThermalConductivity` property on the ThermalAsset attached to a material.
If the underlying asset cannot be edited directly (some built-in assets), the command will:
- Duplicate the thermal asset,
- Set `ThermalConductivity` on the duplicate, and
- Reassign the material to use the duplicated asset.

## Usage
- Method: set_material_thermal_conductivity

### Parameters
| Name       | Type   | Required | Default |
|------------|--------|----------|---------|
| materialId | int    | no / one of | 0     |
| uniqueId   | string | no / one of |       |
| value      | double | yes      |         |

- `materialId` or `uniqueId` must be provided.
- `value` is thermal conductivity in **W/(m·K)**.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_material_thermal_conductivity",
  "params": {
    "materialId": 99146,
    "value": 1.6
  }
}
```

### Example Result (shape)
```json
{
  "ok": true,
  "materialId": 99146,
  "uniqueId": "...",
  "value": 1.6
}
```

## Related
- get_material_asset_properties
- set_material_asset

