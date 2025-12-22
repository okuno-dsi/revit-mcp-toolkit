# sheet.remove_titleblocks_auto

- Category: ViewSheet Ops
- Purpose: Remove all titleblock instances from a sheet (supports dryRun).

## Overview
High-level intent command (Step 5).

- Canonical: `sheet.remove_titleblocks_auto`
- Legacy alias: `remove_titleblocks_auto`

## Usage
- Method: `sheet.remove_titleblocks_auto`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| sheet | object | no* | (active sheet view) |
| dryRun | boolean | no | false |
| sheetId | integer | no (legacy) |  |
| sheetNumber | string | no (legacy) |  |
| uniqueId | string | no (legacy) |  |

Notes:
- `sheet`: `{ id } | { number } | { name } | { uniqueId }`

### Example Request (dryRun)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "sheet.remove_titleblocks_auto",
  "params": {
    "sheet": { "number": "A-101" },
    "dryRun": true
  }
}
```

## Related
- create_sheet
- get_sheets
- delete_sheet
- sheet.place_view_auto

