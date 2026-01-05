# rebar_regenerate_delete_recreate

- Category: Rebar
- Purpose: Delete tool-generated rebars under selected host(s), recreate from current parameters, and update the in-model recipe ledger.

## Overview
- Builds an auto-rebar plan first (same planning inputs as `rebar_plan_auto`).
- For each host (isolated in its own transaction):
  1) Deletes only rebars that are both:
     - “in host” (`RebarHostData.GetRebarsInHost()`), and
     - tagged by `Comments` containing `tag`
  2) Recreates rebars from the plan actions.
  3) Writes a per-host record into the in-model ledger with the computed `signatureSha256`.

## Usage
- Method: `rebar_regenerate_delete_recreate`

### Parameters
| Name | Type | Required | Default | Notes |
|---|---|---:|---:|---|
| hostElementIds | int[] | no |  | If omitted, uses selection (`useSelectionIfEmpty=true`). |
| useSelectionIfEmpty | bool | no | true | Use current selection if `hostElementIds` is empty. |
| profile | string | no |  | Mapping profile name for `RebarMapping.json` (optional). |
| options | object | no |  | Same schema as `rebar_plan_auto.options` (affects planning). |
| tag | string | no | `RevitMcp:AutoRebar` | Delete filter tag (matched against Rebar `Comments`). If omitted, falls back to `options.tagComments`. |
| deleteMode | string | no | `tagged_only` | Only supported mode for safety. |
| storeRecipeSnapshot | bool | no | false | If true, stores the full computed recipe JSON in the ledger (debug; can bloat RVT). |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rebar_regenerate_delete_recreate",
  "params": {
    "useSelectionIfEmpty": true,
    "profile": "default",
    "tag": "RevitMcp:AutoRebar",
    "deleteMode": "tagged_only",
    "options": {
      "mainBarTypeName": "D22",
      "tieBarTypeName": "D10",
      "tagComments": "RevitMcp:AutoRebar"
    }
  }
}
```

## Notes
- This is a **high-risk write** command (it deletes elements), but deletion is intentionally restricted to “tagged-only within host”.
- The ledger DataStorage is created only when missing (inside a transaction); if you only run `rebar_sync_status`, no DataStorage is created.
- Ledger write happens in the same transaction as delete/recreate; if ledger write fails, the host transaction is rolled back.

## Related
- `rebar_sync_status`
- `rebar_plan_auto`
- `rebar_apply_plan`

