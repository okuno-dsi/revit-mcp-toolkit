# get_project_units

- Category: DocumentOps
- Kind: read
- Purpose: Get the project unit settings (FormatOptions) for key SpecTypeId entries.

This is useful to confirm how Revit will **format/display values** (Length/Area/Volume/Angle, etc.) in the UI.

## Command
- Canonical: `doc.get_project_units`
- Alias: `get_project_units`

## Parameters
| Name | Type | Required | Default | Notes |
|---|---|---:|---:|---|
| mode | string | no | `common` | `common` or `all`. |
| specNames | string[] | no | `[]` | SpecTypeId property names (e.g. `["Length","Area","Volume","Angle","Slope"]`). If set, this overrides `mode`. |
| includeLabels | bool | no | `true` | Best-effort labels via `LabelUtils` (may be empty depending on spec/unit). |
| includeExamples | bool | no | `true` | Adds `exampleFromInternal_1` as a quick sanity check. |

## Result
Returns:
- `displayUnitSystem` (e.g., `Metric`/`Imperial`)
- `items[]`: per spec, `specTypeId`, `unitTypeId`, optional labels, and FormatOptions hints

## Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "doc.get_project_units",
  "params": {
    "mode": "common"
  }
}
```

## Notes
- Revit internal units are fixed per spec (e.g., Length=ft, Area=ft², Volume=ft³).
- `exampleFromInternal_1` is `UnitUtils.ConvertFromInternalUnits(1.0, unitTypeId)` (not a guaranteed linear factor for all specs).

