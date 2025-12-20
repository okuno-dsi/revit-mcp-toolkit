# set_material_asset / set_material_structural_asset / set_material_thermal_asset

- Category: ElementOps
- Purpose: Assign a structural or thermal asset to a material.

## Overview
This command assigns an existing PropertySetElement asset (structural or thermal) to a material. It does **not** change individual property values; it just switches which asset the material references.

## Usage
- Method (aliases):
  - `set_material_asset`
  - `set_material_structural_asset`
  - `set_material_thermal_asset`

### Parameters
| Name       | Type   | Required        | Default |
|------------|--------|-----------------|---------|
| materialId | int    | no / one of     | 0       |
| uniqueId   | string | no / one of     |         |
| assetKind  | string | no / see below  |         |
| assetId    | int    | no / one of     | 0       |
| assetName  | string | no / one of     |         |

- `materialId` or `uniqueId` must be provided.
- `assetId` or `assetName` must be provided.
- `assetKind`:
  - When using `set_material_asset`, you **must** set `"structural"` or `"thermal"`.
  - When using `set_material_structural_asset`, `assetKind` defaults to `"structural"`.
  - When using `set_material_thermal_asset`, `assetKind` defaults to `"thermal"`.

### Example Request (thermal asset)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_material_thermal_asset",
  "params": {
    "materialId": 99146,
    "assetId": 198872
  }
}
```

### Example Result (shape)
```json
{
  "ok": true,
  "materialId": 99146,
  "uniqueId": "...",
  "assetKind": "thermal",
  "assetId": 198872,
  "assetName": "コンクリート"
}
```

## Related
- get_material_assets
- list_physical_assets
- list_thermal_assets

## Limitations and recommended workflow

- Some **built-in** or **library** assets may refuse updates to their physical/thermal properties when edited via the API, even if this command successfully assigns them.
- For the **Thermal** tab in particular, you should treat the **Revit Material Browser UI** as the authority for:
  - which thermal asset is assigned, and
  - what the final property values (λ, density, Cp, etc.) should be.
- In practice, use `set_material_asset` mainly to:
  - attach a custom asset that you created/duplicated in the UI, or
  - batch rewire many materials to an already‑configured asset.

Always verify the result in the Material Browser. If Revit’s UI and MCP disagree, **prefer the UI** and adjust assets there first, then use this command as a helper only where it behaves reliably.
