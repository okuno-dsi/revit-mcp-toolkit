# create_duct

- Category: MEPOps
- Purpose: Create Duct in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_duct

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| diameterMm | unknown | no/depends |  |
| ductTypeId | int | no/depends |  |
| heightMm | unknown | no/depends |  |
| levelId | int | no/depends |  |
| systemTypeId | int | no/depends |  |
| widthMm | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_duct",
  "params": {
    "diameterMm": "...",
    "ductTypeId": 0,
    "heightMm": "...",
    "levelId": 0,
    "systemTypeId": 0,
    "widthMm": "..."
  }
}
```

## Related
- create_pipe
- create_cable_tray
- create_conduit
- get_mep_elements
- move_mep_element
- delete_mep_element
- change_mep_element_type
- get_mep_parameters

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "widthMm": {
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
    "levelId": {
      "type": "integer"
    },
    "ductTypeId": {
      "type": "integer"
    },
    "systemTypeId": {
      "type": "integer"
    },
    "heightMm": {
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
    "diameterMm": {
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
