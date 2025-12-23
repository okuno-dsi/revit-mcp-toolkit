# set_material_thermal_properties

- Category: ElementOps
- Purpose: Set Thermal asset properties (ThermalConductivity / Density / SpecificHeat) for a material.

## Overview
Updates the Thermal asset assigned to a material, with best-effort handling for library/read-only assets by duplicating and re-binding an editable asset when needed.

## Usage
- Method: `set_material_thermal_properties`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| materialId | int | no / one of | 0 |
| uniqueId | string | no / one of |  |
| properties | object | yes |  |
| conductivityUnits | string | no | W/(m·K) |
| densityUnits | string | no | kg/m3 |
| specificHeatUnits | string | no | J/(kg·K) |

Notes:
- Inputs are converted to Revit internal units before writing.
- `densityUnits` currently supports SI only: `"kg/m3"` / `"kg/m^3"`.
- `specificHeatUnits` currently supports SI only: `"J/(kg·K)"` (and common variants like `"J/kgK"`).

`properties` keys (case-insensitive; only include what you want to change):
- Thermal conductivity (λ):
  - `ThermalConductivity` / `thermalConductivity` / `lambda`
- Density:
  - `Density` / `density`
- Specific heat:
  - `SpecificHeat` / `specificHeat` / `Cp`

### Example Request (λ = 1.6 W/(m·K))
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_material_thermal_properties",
  "params": {
    "materialId": 7255081,
    "properties": { "ThermalConductivity": 1.6 },
    "conductivityUnits": "W/(m·K)"
  }
}
```

## Related
- set_material_asset
- set_material_thermal_conductivity
- get_material_asset_properties
