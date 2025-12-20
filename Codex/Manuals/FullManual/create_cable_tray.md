# create_cable_tray

- Category: MEPOps
- Purpose: Create Cable Tray in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_cable_tray

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| heightMm | unknown | no/depends |  |
| levelId | int | no/depends |  |
| trayTypeId | int | no/depends |  |
| widthMm | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_cable_tray",
  "params": {
    "heightMm": "...",
    "levelId": 0,
    "trayTypeId": 0,
    "widthMm": "..."
  }
}
```

## Related
- create_duct
- create_pipe
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
    "levelId": {
      "type": "integer"
    },
    "trayTypeId": {
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
