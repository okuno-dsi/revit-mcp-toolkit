# import_rebar_types_from_document

- Category: Rebar
- Purpose: Import `RebarBarType` / `RebarHookType` / `RebarShape` from a donor `.rvt/.rte` into the active document.

## Overview
Some projects (especially architectural/“no reinforcement yet” models) may have **zero** rebar bar types/shapes. In that case, auto-rebar commands will fail with `BAR_TYPE_NOT_FOUND`.

This command imports the minimum “rebar standards” into the project so the auto-rebar workflow can run.

## Usage
- Method: `import_rebar_types_from_document`

### Parameters
| Name | Type | Required | Default | Notes |
|---|---|---:|---:|---|
| dryRun | bool | no | true | If true, opens the donor doc and reports counts but does not modify the model. |
| sourcePath | string | no |  | Donor `.rvt/.rte` path. If omitted, tries common installed templates (best-effort). |
| includeHookTypes | bool | no | true | Copy `RebarHookType` in addition to bar types. |
| includeShapes | bool | no | false | Copy `RebarShape`. Recommended when the project has `rebarShapeCount: 0`. |
| diametersMm | int[] | no |  | Optional filter for `RebarBarType` by diameter (mm). Shapes/hooks are not filtered. |

### Example (dry run)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "import_rebar_types_from_document",
  "params": {
    "dryRun": true,
    "sourcePath": "C:/ProgramData/Autodesk/RVT 2024/Templates/Japanese/Structural Analysis-DefaultJPNJPN.rte",
    "includeHookTypes": true,
    "includeShapes": true
  }
}
```

### Example (apply)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "import_rebar_types_from_document",
  "params": {
    "dryRun": false,
    "sourcePath": "C:/ProgramData/Autodesk/RVT 2024/Templates/Japanese/Structural Analysis-DefaultJPNJPN.rte",
    "includeHookTypes": true,
    "includeShapes": true
  }
}
```

## Notes
- Copies types via `CopyElements` and uses “Use destination types” when duplicate names exist.
- After import, run `list_rebar_bar_types` to verify non-zero counts, then proceed with `rebar_plan_auto` / `rebar_regenerate_delete_recreate`.

