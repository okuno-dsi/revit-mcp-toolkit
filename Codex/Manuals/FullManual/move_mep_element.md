# move_mep_element

- Category: MEPOps
- Purpose: Move Mep Element in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: move_mep_element

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| dx | number | no/depends |  |
| dy | number | no/depends |  |
| dz | number | no/depends |  |
| elementId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "move_mep_element",
  "params": {
    "dx": 0.0,
    "dy": 0.0,
    "dz": 0.0,
    "elementId": 0
  }
}
```

## Related
- create_duct
- create_pipe
- create_cable_tray
- create_conduit
- get_mep_elements
- delete_mep_element
- change_mep_element_type
- get_mep_parameters

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "dz": {
      "type": "number"
    },
    "elementId": {
      "type": "integer"
    },
    "dy": {
      "type": "number"
    },
    "dx": {
      "type": "number"
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
