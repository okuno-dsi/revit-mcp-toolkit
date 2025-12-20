# create_pipe

- Category: MEPOps
- Purpose: Create Pipe in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_pipe

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| diameterMm | unknown | no/depends |  |
| levelId | int | no/depends |  |
| pipeTypeId | int | no/depends |  |
| slopePermil | unknown | no/depends |  |
| systemTypeId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_pipe",
  "params": {
    "diameterMm": "...",
    "levelId": 0,
    "pipeTypeId": 0,
    "slopePermil": "...",
    "systemTypeId": 0
  }
}
```

## Related
- create_duct
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
    "systemTypeId": {
      "type": "integer"
    },
    "slopePermil": {
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
    },
    "pipeTypeId": {
      "type": "integer"
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
