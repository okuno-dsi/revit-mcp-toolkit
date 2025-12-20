# set_element_workset

- Category: WorksetOps
- Purpose: Set Element Workset in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: set_element_workset

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | int | no/depends |  |
| worksetId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_element_workset",
  "params": {
    "elementId": 0,
    "worksetId": 0
  }
}
```

## Related
- get_worksets
- get_element_workset
- create_workset

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "elementId": {
      "type": "integer"
    },
    "worksetId": {
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
