# get_instance_parameters_bulk

- Category: ParamOps
- Purpose: Read instance parameters for many elements (by ids or categories) with paging.

## Overview
Use this command to pull specific parameters from a batch of elements efficiently.

## Usage
- Method: `get_instance_parameters_bulk`

### Parameters
```jsonc
{
  "elementIds": [1001, 1002],
  "categories": [-2000011],
  "paramKeys": [
    "Comments",
    { "name": "終端でのフックの回転" }
  ],
  "page": { "startIndex": 0, "batchSize": 200 }
}
```

- `paramKeys` (required): array of parameter keys.
  - Each item can be a string (treated as `name`), or an object:
    - `name` (string): parameter name
    - `builtInId` (int): BuiltInParameter integer value
    - `guid` (string): shared parameter GUID
- Target selection:
  - `elementIds` (preferred): explicit elementIds to query
  - or `categories`: BuiltInCategory integer values (instance elements only)
  - If neither is provided, the command rejects the request to avoid sweeping the entire model.
- Paging:
  - `page.startIndex` (default 0)
  - `page.batchSize` (default: up to ~500)

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_instance_parameters_bulk",
  "params": {
    "elementIds": [1234567],
    "paramKeys": ["Comments", "Mark"],
    "page": { "startIndex": 0, "batchSize": 200 }
  }
}
```

## Result (example)
```jsonc
{
  "ok": true,
  "items": [
    {
      "ok": true,
      "elementId": 1234567,
      "categoryId": -2000011,
      "typeId": 7654321,
      "params": { "Comments": "..." },
      "display": { "Comments": "..." }
    }
  ],
  "nextIndex": null,
  "completed": true,
  "totalCount": 1
}
```

## Related
- get_param_meta
- get_parameter_identity
- get_type_parameters_bulk
- update_parameters_batch

