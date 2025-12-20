# create_conduit

- Category: MEPOps
- Purpose: Create Conduit in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_conduit

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| conduitTypeId | int | no/depends |  |
| diameterMm | unknown | no/depends |  |
| levelId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_conduit",
  "params": {
    "conduitTypeId": 0,
    "diameterMm": "...",
    "levelId": 0
  }
}
```

## Related
- create_duct
- create_pipe
- create_cable_tray
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
    "conduitTypeId": {
      "type": "integer"
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
    },
    "levelId": {
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
