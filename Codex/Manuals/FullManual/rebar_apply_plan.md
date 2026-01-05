# rebar_apply_plan

- Category: Rebar
- Purpose: Apply an auto rebar plan (or generate+apply from current selection) and create Rebar elements.

## Overview
If `params.plan` is omitted, this command internally runs `rebar_plan_auto` logic on the given `hostElementIds` / selection, then applies it.

Created rebars are tagged with `Comments = options.tagComments` (default `RevitMcp:AutoRebar`), so you can later bulk-update their layout via `rebar_layout_update_by_host` with `filter.commentsTagEquals`.

Beam note (mapping-driven attributes):
- When `plan` is omitted, this command runs the same planning logic as `rebar_plan_auto`.
- If the matched `RebarMapping.json` profile defines beam attribute keys (and `options.beamUseTypeParams=true`), those values can be preferred (best-effort) to override counts/pitch and choose bar types by diameter.
- The same beam geometry options as `rebar_plan_auto` apply when `plan` is omitted (extensions/bends/stirrup start corner).

## Usage
- Method: `rebar_apply_plan`

### Parameters
| Name | Type | Required | Default | Notes |
|---|---|---:|---:|---|
| dryRun | bool | no | false | If true, returns `{dryRun:true, plan:...}` without modifying the model. |
| plan | object | no |  | Plan object returned by `rebar_plan_auto`. |
| hostElementIds | int[] | no |  | Used only when `plan` is omitted (plan will be generated from selection/ids). |
| useSelectionIfEmpty | bool | no | true | Used only when `plan` is omitted. |
| profile | string | no |  | Used only when `plan` is omitted. |
| options | object | no |  | Used only when `plan` is omitted. Same as `rebar_plan_auto.options`. |
| deleteExistingTaggedInHosts | bool | no | false | If true, deletes existing rebar-like elements in each host whose `Comments == plan.tagComments` before creating new ones. |

### Example Request (one-shot)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rebar_apply_plan",
  "params": {
    "useSelectionIfEmpty": true,
    "deleteExistingTaggedInHosts": true,
    "options": {
      "mainBarTypeName": "D29",
      "tieBarTypeName": "D10",
      "beamMainTopCount": 4,
      "beamMainBottomCount": 4,
      "tagComments": "RevitMcp:AutoRebar"
    }
  }
}
```

### Example Request (apply a plan)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rebar_apply_plan",
  "params": {
    "plan": { "planVersion": 1, "hosts": [] }
  }
}
```

## Notes
- Only valid rebar hosts are processed (`RebarHostData.IsValidHost=true`).
- Failures are isolated per host in separate transactions; a host failure rolls back its own created elements.
- For arbitrary placement, you can pass a custom `plan` object whose `actions[].curves` are explicitly defined (mm) instead of relying on the auto planner.

## Related
- `rebar_plan_auto`
- `rebar_layout_update_by_host`
