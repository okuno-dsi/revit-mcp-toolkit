# compare_view_states

- Category: ViewOps
- Purpose: Compare multiple views against a baseline for properties

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: compare_view_states

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| baselineViewId | int | no/depends | 0 |
| includeCategories | bool | no/depends | true |
| includeFilters | bool | no/depends | true |
| includeHiddenElements | bool | no/depends | false |
| includeWorksets | bool | no/depends | true |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "compare_view_states",
  "params": {
    "baselineViewId": 0,
    "includeCategories": false,
    "includeFilters": false,
    "includeHiddenElements": false,
    "includeWorksets": false
  }
}
```

## Related
- get_current_view
- get_view_info
- save_view_state
- restore_view_state
- create_view_plan
- create_section
- create_elevation_view
- get_views

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "baselineViewId": {
      "type": "integer"
    },
    "includeCategories": {
      "type": "boolean"
    },
    "includeHiddenElements": {
      "type": "boolean"
    },
    "includeWorksets": {
      "type": "boolean"
    },
    "includeFilters": {
      "type": "boolean"
    }
  }
}
```

### Result Schema
```json
{
  "type": "object",
  "properties": {},
  "additionalProperties": true
}
```
