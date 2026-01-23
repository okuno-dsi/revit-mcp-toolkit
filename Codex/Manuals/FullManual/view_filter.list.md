# view_filter.list

- Category: ViewFilterOps
- Purpose: List all View Filters (Parameter/Selection) in the project.

## Overview
Lists `ParameterFilterElement` and `SelectionFilterElement` definitions in the active document.

## Usage
- Method: view_filter.list

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| kinds | string[] | no | ["parameter","selection"] |
| namePrefix | string | no |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view_filter.list",
  "params": {
    "kinds": ["parameter"],
    "namePrefix": "A-"
  }
}
```

## Result
- `filters[]`: `{ elementId, uniqueId, kind, name, categories[], ruleCount, elementIdCount }`

## Related
- view_filter.upsert
- view_filter.delete
- view_filter.apply_to_view
- view_filter.remove_from_view
- view_filter.get_order
- view_filter.set_order

