# generate_dwg_merge_script

- Category: DxfOps
- Purpose: Generate an AutoCAD `.scr` script to merge multiple DWGs into one output DWG.

## Overview
Generates a script that:
- Attaches all DWGs under `inputDir` matching `pattern`,
- Reloads + binds XREFs,
- Optionally merges layers,
- Purges, optionally audits,
- Saves as `outputDwg`.

This is intended to be run by AutoCAD (or AutoCadMCP) as a batch merge step after Revit exports multiple DWGs.

## Usage
- Method: `generate_dwg_merge_script` (alias: `gen_dwg_script`)

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| inputDir | string | yes |  |
| pattern | string | no | walls_*.dwg |
| outputDwg | string | no | C:/temp/CadOut/merged.dwg |
| outScript | string | no | (inputDir)/run_merge.scr |
| bindType | string | no | Bind |
| refPathType | string | no | 2 |
| saveAsVersion | string | no | 2018 |
| trustedPaths | string | no | C:/Temp;C:/Temp/CadOut |
| mergeMode | string | no | None |
| layerMapCsv | string | no |  |
| purgeTimes | int | no | 2 |
| audit | bool | no | false |

- `mergeMode`:
  - `"None"`: do not merge layers (default)
  - `"ByFile"`: merge `XREF$0$...` layers into a per-file layer
  - `"Map"`: merge by a CSV map (`layerMapCsv`)

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "generate_dwg_merge_script",
  "params": {
    "inputDir": "C:/temp/CadOut/DWGs",
    "pattern": "4F_Walls_*.dwg",
    "outputDwg": "C:/temp/CadOut/4F_Walls_merged.dwg",
    "mergeMode": "ByFile",
    "purgeTimes": 2,
    "audit": false
  }
}
```

## Related
- generate_dwg_merge_script_manual

