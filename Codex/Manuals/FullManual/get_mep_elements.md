# get_mep_elements

- Category: MEPOps
- Purpose: Get Mep Elements in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_mep_elements

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends |  |
| skip | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_mep_elements",
  "params": {
    "count": 0,
    "skip": 0
  }
}
```

## Related
- create_duct
- create_pipe
- create_cable_tray
- create_conduit
- move_mep_element
- delete_mep_element
- change_mep_element_type
- get_mep_parameters

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "count": {
      "type": "integer"
    },
    "skip": {
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
