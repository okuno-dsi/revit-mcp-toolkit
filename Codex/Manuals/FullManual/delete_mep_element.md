# delete_mep_element

- Category: MEPOps
- Purpose: Delete Mep Element in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: delete_mep_element

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "delete_mep_element",
  "params": {
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
- move_mep_element
- change_mep_element_type
- get_mep_parameters

### Params Schema
```json
{
  "type": "object",
  "properties": {
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
