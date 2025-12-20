# select_elements_by_filter_id

- Category: GeneralOps
- Purpose: Select Elements By Filter Id in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: select_elements_by_filter_id

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| dryRun | bool | no/depends | false |
| levelId | int | no/depends | 0 |
| logic | string | no/depends | all |
| maxCount | int | no/depends | 5000 |
| selectionMode | string | no/depends | replace |
| viewId | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "select_elements_by_filter_id",
  "params": {
    "dryRun": false,
    "levelId": 0,
    "logic": "...",
    "maxCount": 0,
    "selectionMode": "...",
    "viewId": "..."
  }
}
```

## Related
- get_elements_in_view
- get_types_in_view
- select_elements

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "selectionMode": {
      "type": "string"
    },
    "logic": {
      "type": "string"
    },
    "levelId": {
      "type": "integer"
    },
    "dryRun": {
      "type": "boolean"
    },
    "maxCount": {
      "type": "integer"
    },
    "viewId": {
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
