# adjust_grid_extents

- Category: GridOps
- Purpose: Adjust Grid Extents in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: adjust_grid_extents

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| detachScopeBoxForAdjustment | bool | no/depends | false |
| dryRun | bool | no/depends | false |
| includeLinkedModels | bool | no/depends | false |
| mode | string | no/depends | both |
| skipPinned | bool | no/depends | true |
| viewFilter | unknown | no/depends |  |
| viewIds | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "adjust_grid_extents",
  "params": {
    "detachScopeBoxForAdjustment": false,
    "dryRun": false,
    "includeLinkedModels": false,
    "mode": "...",
    "skipPinned": false,
    "viewFilter": "...",
    "viewIds": "..."
  }
}
```

## Related
- get_grids
- create_grids
- update_grid_name
- move_grid
- delete_grid

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "mode": {
      "type": "string"
    },
    "skipPinned": {
      "type": "boolean"
    },
    "includeLinkedModels": {
      "type": "boolean"
    },
    "dryRun": {
      "type": "boolean"
    },
    "detachScopeBoxForAdjustment": {
      "type": "boolean"
    },
    "viewFilter": {
      "type": [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "array",
        "null"
      ]
    },
    "viewIds": {
      "type": [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "array",
        "null"
      ]
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
