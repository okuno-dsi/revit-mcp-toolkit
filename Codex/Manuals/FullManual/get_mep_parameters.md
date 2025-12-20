# get_mep_parameters

- Category: MEPOps
- Purpose: Get Mep Parameters in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_mep_parameters

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends |  |
| elementId | unknown | no/depends |  |
| includeDisplay | bool | no/depends | true |
| includeRaw | bool | no/depends | true |
| includeUnit | bool | no/depends | true |
| namesOnly | bool | no/depends | false |
| siDigits | int | no/depends | 3 |
| skip | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_mep_parameters",
  "params": {
    "count": 0,
    "elementId": "...",
    "includeDisplay": false,
    "includeRaw": false,
    "includeUnit": false,
    "namesOnly": false,
    "siDigits": 0,
    "skip": 0
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
    "elementId": {
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
    "skip": {
      "type": "integer"
    },
    "includeDisplay": {
      "type": "boolean"
    },
    "namesOnly": {
      "type": "boolean"
    },
    "siDigits": {
      "type": "integer"
    },
    "includeRaw": {
      "type": "boolean"
    },
    "count": {
      "type": "integer"
    },
    "includeUnit": {
      "type": "boolean"
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
