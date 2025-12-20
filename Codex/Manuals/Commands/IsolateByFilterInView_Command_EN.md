# isolate_by_filter_in_view (JSON‑RPC)

Purpose
- Robustly isolate elements in a view using flexible filters (categories, classes, and rich parameter rules), while keeping annotations visible by default. Safe for large views via batching and optional view‑template detaching.

Method
- Name: `isolate_by_filter_in_view`
- Transport: JSON‑RPC 2.0 via Revit MCP (`POST /enqueue` then poll, or `POST /rpc/isolate_by_filter_in_view` when configured)

Parameters (object)
- `viewId` (int, optional): Target view. If omitted, uses the active view.
- `uniqueId` (string, optional): Alternative to `viewId`.
- `detachViewTemplate` (bool, optional, default=true): Temporarily detaches any applied view template to allow changes.
- `reset` (bool, optional, default=true): Best‑effort reset (unhide elements + clear graphic overrides) before applying filters.
- `keepAnnotations` (bool, optional, default=true): Do not hide annotations (CategoryType.Annotation). Model elements only are hidden.
- `batchSize` (int, optional, default=1000): HideElements batch size (50..5000).
- `filter` (object): Element selection logic for what to KEEP (when `invertMatch=false`, default) or to HIDE (when `invertMatch=true`).
  - `includeCategoryIds` (int[]): Only these category IDs pass.
  - `excludeCategoryIds` (int[]): These category IDs fail.
  - `includeClasses` (string[]): CLR class names that pass (e.g., "Wall","Floor","FamilyInstance").
  - `excludeClasses` (string[]): CLR class names that fail.
  - `modelOnly` (bool, default=false): Keep/hide only model elements.
  - `logic` (string, default="all"): How to combine parameter rules: "all" (AND) or "any" (OR).
  - `invertMatch` (bool, default=false): If true, elements matching the rules are hidden; otherwise, non‑matching elements are hidden.
  - `parameterRules` (array of objects): Per‑parameter matching rules
    - `target` (string, default="both"): "instance" | "type" | "both"
    - `name` (string, optional): Parameter display name (e.g., "仕上げ", "Type Name").
    - `builtInName` (string, optional): BuiltInParameter enum name (e.g., "SYMBOL_NAME_PARAM").
    - `op` (string, default="eq"): eq|neq|contains|ncontains|regex|gt|gte|lt|lte|in|nin
    - `value` (any): Comparison value (string/number). For `in|nin`, use "a|b|c" pipe‑separated.
    - `caseInsensitive` (bool, default=true): Case handling for string ops.

Return (object)
- `ok` (bool)
- `viewId` (int): Target view id
- `kept` (int): Count of elements that matched and were kept
- `hidden` (int): Count of elements that were hidden
- `total` (int): Total elements considered in the view

Behavior
- Detaches the view template if requested, then optionally resets per‑element hidden/overrides.
- Iterates visible elements in the view and computes matches using category/class rules and parameter rules.
- Hides elements in batches (HideElements) while preserving annotations by default (`keepAnnotations=true`).

Examples
1) Keep only ALC100 walls (注釈保持)
```
{
  "jsonrpc": "2.0",
  "method": "isolate_by_filter_in_view",
  "params": {
    "viewId": 60521780,
    "detachViewTemplate": true,
    "keepAnnotations": true,
    "filter": {
      "includeClasses": ["Wall"],
      "parameterRules": [
        { "target": "type", "builtInName": "SYMBOL_NAME_PARAM", "op": "eq", "value": "ALC100 複合壁" }
      ],
      "logic": "all"
    }
  },
  "id": 1
}
```

2) Keep beams where Type Name contains "RC" and instance parameter "断面" >= 300 (mm)
```
{
  "jsonrpc": "2.0",
  "method": "isolate_by_filter_in_view",
  "params": {
    "viewId": 60521780,
    "filter": {
      "includeClasses": ["FamilyInstance"],
      "parameterRules": [
        { "target": "type", "name": "Type Name", "op": "contains", "value": "RC" },
        { "target": "instance", "name": "断面", "op": "gte", "value": 300 }
      ],
      "logic": "all"
    }
  },
  "id": 2
}
```

3) Hide furniture where マテリアル = "木"（一致を非表示、invertMatch=true）
```
{
  "jsonrpc": "2.0",
  "method": "isolate_by_filter_in_view",
  "params": {
    "viewId": 60521780,
    "filter": {
      "includeClasses": ["FamilyInstance"],
      "parameterRules": [ { "target": "instance", "name": "マテリアル", "op": "eq", "value": "木" } ],
      "invertMatch": true
    }
  },
  "id": 3
}
```

4) Keep slabs (Floor) and rooms (カテゴリーID) with OR logic
```
{
  "jsonrpc": "2.0",
  "method": "isolate_by_filter_in_view",
  "params": {
    "viewId": 60521780,
    "filter": {
      "includeClasses": ["Floor"],
      "includeCategoryIds": [ -2000160 ],  // OST_Rooms
      "logic": "any"
    }
  },
  "id": 4
}
```

Operational Notes
- Annotations: Set `keepAnnotations=false` if 注釈も条件で隠したい場合。
- BuiltInParameter enum names must match Revit API (e.g., "SYMBOL_NAME_PARAM").
- For numeric comparisons, both sides are parsed as double (internal units); adjust values accordingly.
- Use `invertMatch=true` to hide matches (instead of hiding non‑matches).

Troubleshooting
- No effect: a view template may be locking visibility. Ensure `detachViewTemplate=true`.
- Too many hidden: check include/exclude category/class rules and parameter op/value.
- Performance: increase `batchSize` up to 5000, or narrow filters.

