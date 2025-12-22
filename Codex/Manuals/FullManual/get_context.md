# get_context

- Category: MetaOps
- Purpose: Get the current `contextToken` / `contextRevision` (Step 7: drift detection).

## Overview
- Canonical: `help.get_context`
- Legacy alias: `get_context`

This returns a snapshot of:
- active document (title/path/guid)
- active view (id/name/type)
- current selection (ids, optional/truncated)
- `contextRevision` (per-document monotonic counter)
- `contextToken` (hash of doc+view+revision+selection)

Most commands also echo `contextToken` in `result.context.contextToken` (unified response envelope).

## Usage
- Method: `help.get_context`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| includeSelectionIds | boolean | no | true |
| maxSelectionIds | integer | no | 200 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "help.get_context",
  "params": { "includeSelectionIds": true, "maxSelectionIds": 200 }
}
```

### Example Result (shape)
```jsonc
{
  "ok": true,
  "code": "OK",
  "data": {
    "tokenVersion": "ctx.v1",
    "revision": 123,
    "contextToken": "ctx-...",
    "docGuid": "...",
    "docTitle": "...",
    "docPath": "...",
    "activeViewId": 111,
    "activeViewName": "...",
    "activeViewType": "FloorPlan",
    "selectionCount": 2,
    "selectionIdsTruncated": false,
    "selectionIds": [1001, 1002]
  }
}
```

## Using `expectedContextToken`
Many commands accept an optional `expectedContextToken` in `params`.

- If it does not match the current context, the command returns:
  - `code: PRECONDITION_FAILED`
  - `msg: Context token mismatch...`
  - `nextActions` includes `help.get_context`

Notes:
- `help.get_context` / `get_context` always runs even if you mistakenly pass `expectedContextToken` (so recovery is possible).

