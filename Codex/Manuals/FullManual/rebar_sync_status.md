# rebar_sync_status

- Category: Rebar
- Purpose: Check whether selected rebar hosts are “in sync” with the last delete&recreate run by comparing a recipe signature stored in an in-model ledger.

## Overview
- Computes the **current recipe signature** from the same planning logic as `rebar_plan_auto` (no model changes).
- Reads the **Rebar Recipe Ledger** (DataStorage + ExtensibleStorage) and compares:
  - `currentSignature` (computed now)
  - `lastSignature` (stored at the last `rebar_regenerate_delete_recreate`)
- If the ledger does not exist yet, this command returns `hasLedger:false` (and `lastSignature` will be empty).

## Usage
- Method: `rebar_sync_status`

### Parameters
| Name | Type | Required | Default | Notes |
|---|---|---:|---:|---|
| hostElementIds | int[] | no |  | If omitted, uses selection (`useSelectionIfEmpty=true`). |
| useSelectionIfEmpty | bool | no | true | Use current selection if `hostElementIds` is empty. |
| profile | string | no |  | Mapping profile name for `RebarMapping.json` (optional). Affects the signature. |
| options | object | no |  | Same schema as `rebar_plan_auto.options`. Affects the signature. |
| includeRecipe | bool | no | false | If true, returns `currentRecipe` per host (debug). |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rebar_sync_status",
  "params": {
    "useSelectionIfEmpty": true,
    "profile": "default"
  }
}
```

## Notes
- This is **read-only**: it does **not** create the ledger DataStorage.
- “In sync” is computed as `lastSignature == currentSignature` (case-insensitive).
- The signature is derived from a canonical JSON “recipe” that includes:
  - host identity (ElementId/UniqueId/category)
  - requested profile/options
  - resolved mapping values (and stable error codes)
  - planned actions (curves/layout/bar types)

## Related
- `rebar_regenerate_delete_recreate`
- `rebar_plan_auto`
- `rebar_apply_plan`

