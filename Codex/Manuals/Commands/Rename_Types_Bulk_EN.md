# rename_types_bulk (JSON‑RPC)

Purpose
- High‑throughput bulk rename of ElementTypes by explicit mapping (typeId/uniqueId → newName) in a single transaction slice. Eliminates per‑type RPC/transaction overhead and avoids UI stalls. Supports dry‑run and conflict policies.

Method
- Name: `rename_types_bulk`
- Transport: JSON‑RPC 2.0

Parameters (object)
- `items`: array of `{ typeId?:int | uniqueId?:string, newName:string }` (at least one id key required)
- `startIndex?`: int — slice start (default 0)
- `batchSize?`: int — slice size (default = items length)
- `dryRun?`: bool — plan only, no changes
- `conflictPolicy?`: "skip" | "appendNumber" | "fail" (default: "skip")

Return (object)
- `{ ok, processed, renamed, skipped, items:[{ ok, typeId, oldName, newName?, reason? }], nextIndex?, completed, totalCount }`

Examples
1) JSON mapping (direct)
```
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command rename_types_bulk \
  --params '{
    "items": [
      { "typeId": 431338, "newName": "(150mm) RC150" },
      { "uniqueId": "...", "newName": "(190mm) RC150_外断熱" }
    ],
    "conflictPolicy": "skip",
    "dryRun": false
  }'
```

2) CSV mapping via helper script
```
typeId,newName
431338,(150mm) RC150
355234,(外壁) 1-31-1 屋内防水(アスファルト防水) 下部
```
Command (UTF‑8 BOM CSV recommended):
```
pwsh -ExecutionPolicy Bypass -File Manuals/Scripts/rename_types_bulk.ps1 -Port 5210 \
  -CsvPath Work/YourProject_5210/Logs/type_renames.csv -ConflictPolicy skip
```

Notes
- Name conflicts within a category are handled per `conflictPolicy`.
- For large sets, call repeatedly with `startIndex`/`batchSize` or let the wrapper script page the list.
- Use `dryRun:true` to verify the plan before applying.

