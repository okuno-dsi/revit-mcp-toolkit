# delete_rebars

- Category: Rebar
- Purpose: Delete Rebar elements by elementId(s). Alias: `delete_rebar`.

## Overview
- Accepts arbitrary element IDs, but deletes only “Rebar-like” elements:
  - `Rebar`, `RebarInSystem`, `AreaReinforcement`, `PathReinforcement`
- Supports `dryRun` and chunked transactions (`batchSize`) to avoid long transactions.
- For safer workflows, use the router confirm-token flow for high-risk commands:
  - call with `dryRun=true` (optionally `requireConfirmToken=true`) to preview
  - then re-run with `requireConfirmToken=true` + `confirmToken`

## Usage
- Method: `delete_rebars` (alias: `delete_rebar`)

### Parameters
| Name | Type | Required | Default | Notes |
|---|---|---:|---:|---|
| rebarElementIds | int[] | no |  | Target IDs (preferred). |
| elementIds | int[] | no |  | Alias input for convenience. |
| rebarElementId / elementId | int | no |  | Single-id form. |
| useSelectionIfEmpty | bool | no | true | If ids are empty, use current selection. |
| dryRun | bool | no | false | If true, does not delete and returns targets. |
| batchSize | int | no | 200 | Chunk size per transaction. |
| maxIds | int | no | 2000 | Truncate long id arrays in response. |
| detailLimit | int | no | 200 | Include up to N target details in response (dryRun only). |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "delete_rebars",
  "params": {
    "rebarElementIds": [123456, 123457],
    "dryRun": true
  }
}
```

## Related
- `move_rebars`
- `rebar_regenerate_delete_recreate` (tagged-only delete&recreate)

