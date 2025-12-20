# export_dxf_custom_layers

- Category: Export
- Purpose: Export a view to DXF, then remap DXF layer names with custom rules.

## Overview
This command:
1) Duplicates a view (with detailing),
2) Exports it as DXF using Revit’s exporter,
3) Rewrites DXF layers using `netDxf` (rename / delete / merge).

## Usage
- Method: `export_dxf_custom_layers`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| viewId | int | no | (active view) |
| viewUniqueId | string | no |  |
| outputPath | string | yes |  |
| baseSetup | string | no |  |
| merge | bool | no | true |
| defaultLayer | string | no |  |
| maps | object[] | no | [] |

`maps[]` items:
- Rename rule: `{ "from": "A-DOOR*", "to": "MY-DOOR" }`
- Delete rule: `{ "from": "ANNO-TEMP", "action": "delete" }`

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "export_dxf_custom_layers",
  "params": {
    "viewId": 11121378,
    "outputPath": "C:/temp/out/4F_Walls.dxf",
    "merge": true,
    "defaultLayer": "0",
    "maps": [
      { "from": "A-DOOR*", "to": "DOOR" },
      { "from": "A-WALL*", "to": "WALL" },
      { "from": "ANNO-TEMP", "action": "delete" }
    ]
  }
}
```

## Related
- export_dwg
- export_curves_to_dxf

