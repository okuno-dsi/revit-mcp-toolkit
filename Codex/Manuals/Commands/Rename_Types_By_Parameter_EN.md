# rename_types_by_parameter (JSON‑RPC)

Purpose
- Bulk rename ElementType names based on a parameter value (readable or raw), with flexible rules that add a prefix and/or suffix. Works across categories, or limited to a view or a set of categories/typeIds. Supports dry‑run and time slicing (startIndex / batchSize / nextIndex).

Method
- Name: `rename_types_by_parameter`
- Transport: JSON‑RPC 2.0 (enqueue/poll or direct rpc depending on setup)

Parameters (object)
- `scope?`: "all" | "in_view" (default: "all")
- `viewId?`: int (required when `scope:"in_view"`)
- `categories?`: int[] (BuiltInCategory ids) — limit to these categories
- `typeIds?`: int[] — explicit ElementType ids to rename
- `parameter`: object — how to read the decision key
  - `builtInId?`: int (prefer when known)
  - `builtInName?`: string (BuiltInParameter enum name)
  - `guid?`: string
  - `name?`: string (localized display name; fallback only)
  - `useDisplay?`: bool (default true) — compare display string (`AsValueString()`) instead of raw
  - `op?`: "eq" | "contains" (default: "eq")
  - `caseInsensitive?`: bool (default true)
- `rules`: array of objects — how to transform names
  - `when`: string — value to match (respecting `op` and `caseInsensitive`); use `"*"` to match any value
  - `prefix?`: string — applied before the base name
  - `suffix?`: string — applied after the base name
  - Both `prefix` / `suffix` support tokens: `{value}` (raw), `{display}` (formatted), `{display_no_space}` (formatted without spaces)
- `stripPrefixes?`: string[] — prefixes to remove before applying new prefix (冪等用)
  - Default includes variants like: `"(外壁) ", "（外壁） ", "(内壁) ", "（内壁） ", "外壁 ", "内壁 "` and their space‑less forms
- `startIndex?`: int, `batchSize?`: int — time slicing
- `dryRun?`: bool

Return (object)
- `{ ok, processed, renamed, skipped, items:[{ typeId, oldName, newName?, reason? }], nextIndex?, completed, totalCount }`

Examples
1) Walls: prefix by Function (JP: 機能)
```
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command rename_types_by_parameter \
  --params '{
    "scope":"all",
    "categories":[-2000011],
    "parameter":{"name":"機能","useDisplay":true,"op":"eq","caseInsensitive":true},
    "rules":[{"when":"外部","prefix":"(外壁) "},{"when":"内部","prefix":"(内壁) "}],
    "stripPrefixes":["(外壁) ","（外壁） ","(内壁) ","（内壁） ","外壁 ","内壁 "],
    "startIndex":0,
    "batchSize":400
  }'
```

2) Floors: prefix by thickness display (no‑space)
```
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command rename_types_by_parameter \
  --params '{
    "scope":"all",
    "categories":[-2000032],
    "parameter":{"name":"厚さ","useDisplay":true,"op":"contains","caseInsensitive":true},
    "rules":[{"when":"*","prefix":"({display_no_space}) "}],
    "startIndex":0,
    "batchSize":400
  }'
```
Note: Floors often lack a stable thickness parameter; prefer a precomputed mapping or a category‑specific helper when available.

Notes
- If response includes `nextIndex`, call again with `startIndex=nextIndex` until `completed:true`.
- Conflicts (duplicate names) are skipped with `reason="name_conflict"`.
- Prefix removal is applied before composing new names so repeated runs are safe (idempotent).

