# rebar_spacing_check

- Category: Rebar
- Kind: read
- Purpose: Measure actual center-to-center spacing between existing rebars (model geometry) and validate against `RebarBarClearanceTable.json`.

## Notes
- This command is intended for **single bars** (non-array rebars). For rebar sets (stirrups/ties), use `rebar_layout_inspect` for spacing rules.
- Pairwise distance is measured from the longest centerline segment of each bar (best-effort) and only between nearly-parallel bars (`parallelDotMin`).
- Requirements are taken from `RebarBarClearanceTable.json` (diameter → required center-to-center mm). For mixed diameters, uses `max(reqA, reqB)`.

## Parameters
| Name | Type | Required | Default | Notes |
|---|---|---:|---:|---|
| hostElementIds | int[] | no |  | If provided, collects rebars hosted by these elements. |
| rebarElementIds | int[] | no |  | Direct rebar ids to check (if `hostElementIds` is empty). |
| useSelectionIfEmpty | bool | no | true | If both id arrays are empty, uses current selection (rebars → rebar ids, others → host ids). |
| filter.commentsTagEquals | string | no |  | If set, only rebars whose `Comments` equals this value are checked. |
| parallelDotMin | number | no | 0.985 | Only compares bars with `abs(dot(dirA,dirB)) >= parallelDotMin`. |
| maxPairs | int | no | 20000 | Maximum number of pairs analyzed per host group (rest are skipped). |
| includePairs | bool | no | false | If true, includes a limited list of violation pairs in the response. |
| pairLimit | int | no | 50 | Max number of violations returned when `includePairs=true`. |

## Example
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rebar_spacing_check",
  "params": {
    "useSelectionIfEmpty": true,
    "filter": { "commentsTagEquals": "RevitMcp:AutoRebar" },
    "includePairs": true,
    "pairLimit": 30
  }
}
```

## Response (high level)
- `clearanceTable`: load status of the clearance table (path/sha8/count).
- `groups[]`: per-host group result:
  - `pairwise.minDistanceMm`: minimum measured distance among analyzed pairs (mm).
  - `pairwise.violationsCount`: count of pairs that violate the required center-to-center distance.
  - `violatingRebarIds`: union of rebar ids participating in any violation (useful for coloring via visual overrides).
  - `violations[]` (optional): worst violations with `distanceMm`, `requiredPairCcMm`, `shortageMm`.
