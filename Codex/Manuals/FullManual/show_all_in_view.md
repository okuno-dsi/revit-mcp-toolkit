# show_all_in_view

- Category: ViewOps
- Purpose: "show_all_in_view" non-freezing + UI-like unhide (batched) + always Regenerate

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: show_all_in_view

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| activateView | bool | no/depends | false |
| batchSize | int | no/depends | 400 |
| clearElementOverrides | bool | no/depends | false |
| detachViewTemplate | bool | no/depends | true |
| includeTempReset | bool | no/depends | true |
| maxMillisPerTx | int | no/depends | 3000 |
| refreshView | bool | no/depends | true |
| resetFilterVisibility | bool | no/depends | true |
| resetHiddenCategories | bool | no/depends | true |
| resetWorksetVisibility | bool | no/depends | true |
| startIndex | int | no/depends | 0 |
| unhideElements | bool | no/depends | false |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "show_all_in_view",
  "params": {
    "activateView": false,
    "batchSize": 0,
    "clearElementOverrides": false,
    "detachViewTemplate": false,
    "includeTempReset": false,
    "maxMillisPerTx": 0,
    "refreshView": false,
    "resetFilterVisibility": false,
    "resetHiddenCategories": false,
    "resetWorksetVisibility": false,
    "startIndex": 0,
    "unhideElements": false
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
    "startIndex": {
      "type": "integer"
    },
    "detachViewTemplate": {
      "type": "boolean"
    },
    "refreshView": {
      "type": "boolean"
    },
    "clearElementOverrides": {
      "type": "boolean"
    },
    "unhideElements": {
      "type": "boolean"
    },
    "includeTempReset": {
      "type": "boolean"
    },
    "resetFilterVisibility": {
      "type": "boolean"
    },
    "batchSize": {
      "type": "integer"
    },
    "maxMillisPerTx": {
      "type": "integer"
    },
    "activateView": {
      "type": "boolean"
    },
    "resetWorksetVisibility": {
      "type": "boolean"
    },
    "resetHiddenCategories": {
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
