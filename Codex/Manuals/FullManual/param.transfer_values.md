# param.transfer_values

- Category: ParamOps
- Purpose: Transfer parameter values from a source parameter to a target parameter across elements (selection/ids/categories/all) with string operations.

## Overview
This command copies values from a **source** parameter to a **target** parameter on each element, with optional string operations (overwrite/append/replace/search). It supports instance/type resolution and can run on selection, explicit ids, categories, or all elements.

## Usage
- Method: param.transfer_values
- Aliases: param_transfer_values, transfer_parameter_values

### Parameters (top-level)
```jsonc
{
  "scope": "selection",             // optional, for readability only
  "elementIds": [1001, 1002],         // optional (overrides selection)
  "categoryIds": [2000011],           // optional
  "categoryNames": ["構造鉄筋"],       // optional
  "allowAll": false,                  // optional; if true, all elements in doc

  "source": { "paramName": "ホスト カテゴリ" },
  "target": { "paramName": "コメント" },

  "stringOp": "overwrite",           // overwrite|append|replace|search
  "appendSeparator": " / ",           // used when stringOp=append
  "searchText": "RC",                // used when replace/search
  "replaceText": "SRC",              // used when replace
  "searchScope": "source",            // source|target|either
  "replaceScope": "source",           // source|target
  "caseSensitive": false,

  "sourceTarget": "auto",            // auto|instance|type
  "targetTarget": "auto",            // auto|instance|type
  "preferSource": "instance",         // when auto and both exist
  "preferTarget": "instance",

  "dryRun": false,
  "nullAsError": true,
  "maxElements": 0,
  "maxMillisPerTx": 3000
}
```

### Parameter selectors (source/target)
Each of `source` / `target` accepts a parameter key in one of the following forms:

```jsonc
{ "paramName": "コメント" }
{ "name": "コメント" }
{ "builtInId": -1002001 }
{ "builtInName": "ALL_MODEL_INSTANCE_COMMENTS" }
{ "guid": "00000000-0000-0000-0000-000000000000" }
```

### String operations
- `overwrite` (default): write source value to target.
- `append`: write target + separator + source.
- `replace`: replace `searchText` with `replaceText` in `replaceScope` string.
- `search`: only write when `searchText` matches (searchScope). Otherwise skip.

### Selection / scope resolution
Priority order:
1) `elementIds`
2) current selection
3) `categoryIds` / `categoryNames`
4) `allowAll=true` (all elements)

### Output (summary)
```jsonc
{
  "ok": true,
  "updatedCount": 25,
  "failedCount": 2,
  "skippedCount": 3,
  "scopeUsed": "selection",
  "truncated": false,
  "notes": ["auto resolution prefers instance when both instance/type parameters exist (preferSource/Target default: instance)."],
  "items": [
    { "ok": true, "elementId": 1001, "sourceResolvedOn": "instance", "targetResolvedOn": "type" },
    { "ok": false, "msg": "Target parameter 'コメント' is read-only." }
  ]
}
```

## Notes
- When `sourceTarget` / `targetTarget` are `auto`, instance parameters are preferred if both instance and type exist.
- `nullAsError=true` treats missing/empty values as failures.
- Long batches are time-sliced by `maxMillisPerTx` to avoid long transactions.
- For string targets, `stringOp` controls write logic; non-string targets only support `overwrite`.

