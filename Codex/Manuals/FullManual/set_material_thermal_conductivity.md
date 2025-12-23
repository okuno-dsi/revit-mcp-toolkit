# set_material_thermal_conductivity

- Category: ElementOps
- Purpose: Set the thermal conductivity (λ) of a material's thermal asset.

## Overview
This command updates the `ThermalConductivity` property on the ThermalAsset attached to a material.

To make the update persist reliably, the add-in tries up to 3 strategies:
1) Edit the current ThermalAsset directly
2) Duplicate the `PropertySetElement`, edit the duplicate, and re-bind the material via `Material.SetMaterialAspectByPropertySet(...)`
3) Create a new `ThermalAsset` + `PropertySetElement` and re-bind the material

## Usage
- Method: set_material_thermal_conductivity

### Parameters
| Name       | Type   | Required | Default |
|------------|--------|----------|---------|
| materialId | int    | no / one of | 0     |
| uniqueId   | string | no / one of |       |
| value      | double | yes      |         |
| units      | string | no       | W/(m·K) |

- `materialId` or `uniqueId` must be provided.
- `value` is thermal conductivity in the specified `units`.
  - Default: **W/(m·K)**
  - Supported: `"W/(m·K)"` (and common variants like `"W/mK"`), `"BTU/(h·ft·°F)"` (and common variants)

## If the material has no Thermal asset
If `get_material_asset_properties` returns `"thermal": null`, you must assign a thermal asset first, then set λ.

Practical workflow:
1) Pick any thermal asset from `list_thermal_assets` (it will be used as a temporary template if Revit refuses direct edits).
2) Assign it via `set_material_thermal_asset`.
3) Call `set_material_thermal_conductivity`.

In many cases, this command will end up creating a new editable asset and rebinding the material (strategy `create_new`) to make the change persist.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_material_thermal_conductivity",
  "params": {
    "materialId": 99146,
    "value": 1.6,
    "units": "W/(m·K)"
  }
}
```

### Example Result (shape)
```json
{
  "ok": true,
  "materialId": 99146,
  "uniqueId": "...",
  "strategy": "direct_edit",
  "normalized": { "W_per_mK": 1.6 },
  "stored": { "W_per_mK": 1.6, "internalValue": 0.48768 },
  "thermalAsset": { "assetId": 198872, "assetName": "..." },
  "debug": []
}
```

## Related
- get_material_asset_properties
- set_material_asset
