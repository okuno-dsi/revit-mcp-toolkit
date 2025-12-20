# select_elements

- Category: GeneralOps
- Purpose: Select Elements in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: select_elements

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | unknown | no/depends |  |
| elementIds | unknown | no/depends |  |
| zoomTo | bool | no/depends | false |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "select_elements",
  "params": {
    "elementId": "...",
    "elementIds": "...",
    "zoomTo": false
  }
}
```

## Related
- get_elements_in_view
- get_types_in_view
- select_elements_by_filter_id

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
    "zoomTo": {
      "type": "boolean"
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
