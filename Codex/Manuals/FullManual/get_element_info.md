# get_element_info

- Category: Misc
- Purpose: elementId/uniqueId系から要素情報を取得（mm/ft両立）

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_element_info

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | unknown | no/depends |  |
| elementIds | unknown | no/depends |  |
| includeVisual | bool | no/depends | false |
| rich | bool | no/depends | false |
| uniqueId | unknown | no/depends |  |
| uniqueIds | unknown | no/depends |  |
| viewId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_element_info",
  "params": {
    "elementId": "...",
    "elementIds": "...",
    "includeVisual": false,
    "rich": false,
    "uniqueId": "...",
    "uniqueIds": "...",
    "viewId": 0
  }
}
```

## Related
- get_selected_element_ids
- stash_selection
- restore_selection

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "elementId": {
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
    "elementIds": {
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
    "rich": {
      "type": "boolean"
    },
    "uniqueId": {
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
    "uniqueIds": {
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
    "includeVisual": {
      "type": "boolean"
    },
    "viewId": {
      "type": "integer"
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
