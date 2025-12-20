# set_mep_parameter

- Category: MEPOps
- Purpose: Set Mep Parameter in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: set_mep_parameter

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | int | no/depends |  |
| paramName | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_mep_parameter",
  "params": {
    "elementId": 0,
    "paramName": "..."
  }
}
```

## Related
- create_duct
- create_pipe
- create_cable_tray
- create_conduit
- get_mep_elements
- move_mep_element
- delete_mep_element
- change_mep_element_type

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "paramName": {
      "type": "string"
    },
    "elementId": {
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
