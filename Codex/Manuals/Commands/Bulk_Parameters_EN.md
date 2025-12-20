# Bulk Parameter Retrieval (Types and Instances)

Overview
- Two bulk endpoints reduce round‑trips on large projects and return SI‑normalized values (via UnitHelper) plus display strings.
- Use `get_types_in_view` to discover `typeIds` in a view, then call `get_type_parameters_bulk`.

Commands
- `get_type_parameters_bulk`
- `get_instance_parameters_bulk`

Shared Key Model
- `paramKeys`: array of keys to resolve. Each key can be any of:
  - `{ "builtInId": int }` (preferred)
  - `{ "guid": "…" }`
  - `{ "name": "…" }`

get_type_parameters_bulk
- Params:
  - `categories?`: int[] BuiltInCategory ids, or `typeIds?`: int[]
  - `paramKeys`: key array
  - `page?`: `{ startIndex, batchSize }`
- Result:
  - `{ ok, items: [{ ok, typeId, typeName, categoryId?, params{}, display?, errors?[] }], nextIndex?, completed, totalCount }`
- Notes:
  - Double parameters are normalized to SI (Length=mm/Area=m2/Volume=m3/Angle=deg) using UnitHelper; `display` returns `AsValueString()`.
  - If a Double parameter lacks a spec but looks like structural section keys (H/B/tw/tf), a length heuristic (mm) is applied.

get_instance_parameters_bulk
- Params:
  - `elementIds?`: int[] or `categories?`: int[] (one is required)
  - `paramKeys`: key array
  - `page?`: `{ startIndex, batchSize }`
- Result:
  - `{ ok, items: [{ ok, elementId, categoryId?, familyName?, typeId?, params{}, display?, errors?[] }], nextIndex?, completed, totalCount }`

Examples
```json
// Types (Structural Framing H/B/tw/tf)
{
  "categories": [-2001320],
  "paramKeys": [{"name":"H"},{"name":"B"},{"name":"tw"},{"name":"tf"}],
  "page": {"startIndex": 0, "batchSize": 300}
}

// Instances (selected elements)
{
  "elementIds": [5086904, 5086908],
  "paramKeys": [{"name":"Comments"},{"builtInId": -1002001}],
  "page": {"startIndex": 0, "batchSize": 200}
}

// Instances (Rooms grouped by live load / 積載荷重)
// Faster than calling get_room_params for each roomId.
{
  "categories": [-2000160],             // OST_Rooms
  "paramKeys": [{ "name": "積載荷重" }],
  "page": { "startIndex": 0, "batchSize": 500 }
}
```

Client Tips
- Prefer `builtInId`/`guid` over `name` to avoid locale issues.
- Page with 200–500 items per batch in very large models.
- When only typeIds are needed from a view, call `get_types_in_view` first.

## Timeout Mitigation via Chunking

Long‑running reads can time out when requesting too many keys at once or when the model/UI is busy. Split work into small, fast chunks that each finish comfortably under your request timeout and aggregate the results client‑side.

- Why it helps
  - Keeps each HTTP/JSON‑RPC round trip short (< 8–10 s), reducing the chance of queue starvation or UI locks.
  - Retries become cheaper; a failed chunk can be re‑run without redoing prior work.
- How to chunk
  - Build `paramKeys` from a prior metadata pass (e.g., `get_param_meta` on the target element/type) and then send them in slices (e.g., 40–80 keys per call).
  - For instances: `get_instance_parameters_bulk { elementIds:[…], paramKeys:[slice], page:{startIndex:0,batchSize:1} }` and merge `items[0].params/display`.
  - For types: `get_type_parameters_bulk { typeIds:[…], paramKeys:[slice], page:{…} }` and merge per typeId.
- Suggested client defaults
  - `wait-seconds`: 3–5, `timeout-sec`: 8–12 per chunk; adjust chunk size to keep below timeout.
  - Prefer stable keys (`builtInId`/`guid`) in `paramKeys`; fall back to `name` only when necessary.
- Example (pseudo)
  - 1) ids = get_param_meta(element).parameters.map(name/id)
  - 2) for keys in chunk(ids, 50): call get_instance_parameters_bulk(keys)
  - 3) fold items[0].params/display into aggregate maps and then write CSV

This pattern proved effective in busy sessions (multi‑instance Revit, large projects) to avoid sporadic timeouts while keeping total wall‑clock time low.


## Robust boolean/on-off parameter reads

Boolean-like instance parameters can surface as different storage types or localized display strings depending on families/templates. To avoid misreads:

- Prefer get_instance_parameters_bulk with explicit paramKeys targeting a stable key (uiltInId or guid when available). Use 
ame only as a fallback.
- Interpret values defensively:
  - If value is boolean: True/False directly.
  - If integer/double: treat nonzero as ON.
  - If string: trim and compare case-insensitively against "true", "yes", "on", and common locale strings such as "はい" (JP). Add "checked" as a tolerant UI-export synonym.
  - If value missing, consult display[paramName] with the same comparisons; some sources expose localized text only.
- Normalize to a boolean flag for downstream logic. Pseudo:

`
isOn =
  (v is bool and v) or
  (v is int and v != 0) or
  (v is float and v != 0.0) or
  (is_string_true(v) or is_string_true(display))

is_string_true(s) := lower(trim(s)) in {"true","yes","on","はい","checked"}
`

This approach avoids locale and storage-type drift and matches typical Revit parameter projections.
