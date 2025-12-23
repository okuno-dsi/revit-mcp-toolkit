# sheet.inspect

- Category: Diagnostics
- Purpose: Inspect a sheet (titleblocks, sheet outline, viewports list).

## Overview
- Canonical: `sheet.inspect`
- Legacy alias: `sheet_inspect`

## Parameters
| Name | Type | Required | Default |
|---|---|---:|---:|
| sheet | object | no | active sheet |
| sheetId | integer | no | active sheet |
| sheetNumber | string | no | active sheet |
| includeTitleblocks | boolean | no | true |
| includeViewports | boolean | no | true |

`sheet` object (recommended):
- `{ id }`
- `{ uniqueId }`
- `{ number }` (sheet number)
- `{ name }`

## Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "sheet.inspect",
  "params": { "sheet": { "number": "A-101" } }
}
```

## Output (high level)
- `data.sheet`: sheetId/number/name/uniqueId
- `data.outlineMm`: outline rectangle in mm (min/max/width/height)
- `data.needsFallbackSheetSize`: `true` if outline size could not be inferred reliably
- `data.titleblocks`: count + type info (optional)
- `data.viewports`: viewport list (viewportId/viewId/boxCenter/boxOutline/rotation)

