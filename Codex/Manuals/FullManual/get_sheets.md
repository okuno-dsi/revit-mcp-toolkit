# get_sheets

- Category: ViewOps
- Purpose: Get Sheets in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_sheets

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends |  |
| includePlacedViews | bool | no/depends | false |
| nameContains | string | no/depends |  |
| namesOnly | bool | no/depends | false |
| sheetNumberContains | string | no/depends |  |
| skip | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_sheets",
  "params": {
    "count": 0,
    "includePlacedViews": false,
    "nameContains": "...",
    "namesOnly": false,
    "sheetNumberContains": "...",
    "skip": 0
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
    "nameContains": {
      "type": "string"
    },
    "skip": {
      "type": "integer"
    },
    "includePlacedViews": {
      "type": "boolean"
    },
    "namesOnly": {
      "type": "boolean"
    },
    "count": {
      "type": "integer"
    },
    "sheetNumberContains": {
      "type": "string"
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
