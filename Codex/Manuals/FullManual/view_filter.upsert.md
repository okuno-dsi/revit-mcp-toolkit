# view_filter.upsert

- Category: ViewFilterOps
- Purpose: Create or update a View Filter definition (project-wide).

## Overview
Creates or updates a filter definition:
- `kind="parameter"` → `ParameterFilterElement` (recommended)
- `kind="selection"` → `SelectionFilterElement` (special cases)

This command performs strict validation to avoid common Revit exceptions:
- categories must be valid BuiltInCategory names
- parameters must be filterable-in-common for the selected categories
- `parameterName` is best-effort and may be rejected as ambiguous (prefer `builtInParameter` or `sharedParameterGuid`)

## Usage
- Method: view_filter.upsert

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| definition | object | yes |  |

### Example (parameter filter, string contains)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view_filter.upsert",
  "params": {
    "definition": {
      "kind": "parameter",
      "name": "A-WALLS-RC",
      "categories": ["OST_Walls"],
      "logic": "and",
      "rules": [
        {
          "parameter": {
            "type": "builtInParameter",
            "builtInParameter": "ALL_MODEL_INSTANCE_COMMENTS"
          },
          "operator": "contains",
          "value": { "kind": "string", "text": "RC", "caseSensitive": false }
        }
      ]
    }
  }
}
```

### Example (selection filter)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view_filter.upsert",
  "params": {
    "definition": {
      "kind": "selection",
      "name": "TEMP-HIGHLIGHT-001",
      "elementIds": [123, 456]
    }
  }
}
```

## Related
- view_filter.list
- view_filter.delete
- view_filter.apply_to_view

