# place_roof_brace_from_prompt

- Category: ElementOps
- Purpose: Place roof braces from beams using an interactive prompt-driven UI.

## Overview
This command opens a WPF UI inside Revit to preview and execute brace placement based on detected beam bays.

Notes:
- This is an **interactive** command (requires user interaction in Revit).
- The command requires `params.levelName`.

## Usage
- Method: `place_roof_brace_from_prompt`

### Parameters (minimum)
| Name | Type | Required | Default |
|---|---|---|---|
| levelName | string | yes |  |
| dryRun | bool | no | false |

Other options are accepted and may vary by implementation (beam filters, mark filters, brace type list, etc.).

### Brace type filtering (optional)
You can filter the brace type dropdown by passing any of the following:
- `braceTypeFilterParam` (ParamResolver spec, e.g. `{ "paramName": "符号" }`)
- `braceTypeContains` / `braceTypeExclude`
- `braceTypeFamilyContains`
- `braceTypeNameContains`

These filters are evaluated with **AND** logic. If `braceTypeFilterParam` is not provided, the command falls back to `Symbol(符号) → Type Mark → TypeName`.

### Example Request (dry run)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "place_roof_brace_from_prompt",
  "params": {
    "levelName": "RFL",
    "dryRun": true
  }
}
```

## Related
- get_structural_framing
- get_grids
