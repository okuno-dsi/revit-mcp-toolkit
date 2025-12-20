# build_views_by_ruleset

- Category: ViewOps
- Purpose: Build Views By Ruleset in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: build_views_by_ruleset

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| detachViewTemplate | bool | no/depends | true |
| keepAnnotations | bool | no/depends | false |
| sourceViewId | int | no/depends | 0 |
| withDetailing | bool | no/depends | true |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "build_views_by_ruleset",
  "params": {
    "detachViewTemplate": false,
    "keepAnnotations": false,
    "sourceViewId": 0,
    "withDetailing": false
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
- compare_view_states

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "sourceViewId": {
      "type": "integer"
    },
    "withDetailing": {
      "type": "boolean"
    },
    "keepAnnotations": {
      "type": "boolean"
    },
    "detachViewTemplate": {
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
