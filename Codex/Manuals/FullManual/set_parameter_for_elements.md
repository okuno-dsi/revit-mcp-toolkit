# set_parameter_for_elements

- Category: Parameters
- Purpose: Bulk set a single parameter to the same value for many elements by ElementId.

## Overview
This command writes **one parameter** to **one value** across a list of elements, in a single MCP call and a single Revit transaction (time-sliced only on errors or long batches).  
It is designed for AI agents and scripts that need to apply a simple flag or label to many elements efficiently.

## Usage
- Method: set_parameter_for_elements

### Parameters
```jsonc
{
  "elementIds": [1001, 1002, 1003],
  "param": {
    "name": "コメント",
    "builtIn": null,
    "sharedGuid": null
  },
  "value": {
    "storageType": "String",
    "stringValue": "外壁"
  },
  "options": {
    "stopOnFirstError": false,
    "skipReadOnly": true,
    "ignoreMissingOnElement": true
  }
}
```

- `elementIds` (array<int>, required):
  - Revit element IDs (`ElementId.IntegerValue`) to update.

- `param` (object, required):
  - How to identify the parameter. At least one of the following must be provided:
    - `name` (string): parameter display name (e.g., `"コメント"`).
    - `builtIn` (string): `BuiltInParameter` enum name (e.g., `"WALL_ATTR_FIRE_RATING"`).
    - `sharedGuid` (string): GUID string of a shared parameter.
  - Priority: `builtIn` > `sharedGuid` > `name`.

- `value` (object, required):
  - `storageType`: `"String" | "Integer" | "Double" | "ElementId"`
  - Depending on `storageType`, exactly one of the following must be set:
    - `stringValue`: string (for `"String"`)
    - `intValue`: integer (for `"Integer"`)
    - `doubleValue`: number (for `"Double"`, in external units aligned with `UnitHelper` ? typically SI)
    - `elementIdValue`: integer (for `"ElementId"`, target ElementId.IntegerValue)

- `options` (object, optional):
  - `stopOnFirstError` (bool, default: `false`)
    - When `true`, stops processing remaining elements when the first failure occurs.
  - `skipReadOnly` (bool, default: `true`)
    - When `true`, read-only parameters are reported as failures for those elements but do not stop the batch (unless `stopOnFirstError` is also true).
  - `ignoreMissingOnElement` (bool, default: `true`)
    - When `true`, elements that do not have the parameter are recorded as failures but the command can still return `ok: true` if at least one element was updated.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": "set-comment-walls-3f",
  "method": "set_parameter_for_elements",
  "params": {
    "elementIds": [1001, 1002, 1003],
    "param": {
      "name": "コメント"
    },
    "value": {
      "storageType": "String",
      "stringValue": "外壁"
    },
    "options": {
      "stopOnFirstError": false,
      "skipReadOnly": true,
      "ignoreMissingOnElement": true
    }
  }
}
```

### Example: setting an ElementId parameter (Rebar hook type)
Some parameters store an `ElementId` (e.g. Rebar hook type parameters). In that case:

```jsonc
{
  "elementIds": [5945871],
  "param": { "name": "始端のフック" },
  "value": { "storageType": "ElementId", "elementIdValue": 4857530 }
}
```

## Result

### Success
```jsonc
{
  "ok": true,
  "msg": "Updated 120 elements. 8 elements failed.",
  "stats": {
    "totalRequested": 128,
    "successCount": 120,
    "failureCount": 8
  },
  "results": [
    {
      "elementId": 1001,
      "ok": true,
      "scope": "Instance",
      "msg": "Updated",
      "resolvedBy": "name:コメント"
    },
    {
      "elementId": 1002,
      "ok": false,
      "scope": "Instance",
      "msg": "Parameter 'コメント' is read-only.",
      "resolvedBy": "name:コメント"
    }
  ]
}
```

### Fatal error
```json
{
  "ok": false,
  "msg": "value.storageType が必要です。String/Integer/Double/ElementId のいずれかを指定してください。",
  "stats": {
    "totalRequested": 3,
    "successCount": 0,
    "failureCount": 0
  },
  "results": []
}
```

## Notes

- This command focuses on **instance parameters** but can also touch type parameters.
  - When a type parameter is updated, `scope` is `"Type"` in the per-element result.
  - Changing a type parameter affects all instances of that type in the model, not just the listed `elementIds`.
- Value interpretation for `Double` follows `UnitHelper.TrySetParameterByExternalValue`, which expects external (typically SI-normalized) values for numeric parameters.
- Use together with `get_elements_by_category_and_level` to implement workflows like:
  - “All walls on Level 3: コメント = 外壁”
  - “All structural frames on Level 1: shared param 耐火区分 = 2”

## Related
- update_parameters_batch
- get_elements_by_category_and_level
- set_room_param
- set_level_parameter
