# set_stair_flight_parameters

- Category: ElementOps
- Purpose: Set Stair Flight Parameters in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: set_stair_flight_parameters

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | int | no/depends |  |
| flightIndex | int | no/depends |  |
| paramName | string | no/depends |  |
| value | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_stair_flight_parameters",
  "params": {
    "elementId": 0,
    "flightIndex": 0,
    "paramName": "...",
    "value": "..."
  }
}
```

## Related
- get_materials
- get_material_parameters
- list_material_parameters
- update_material_parameter
- duplicate_material
- rename_material
- delete_material
- create_material

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "elementId": {
      "type": "integer"
    },
    "paramName": {
      "type": "string"
    },
    "flightIndex": {
      "type": "integer"
    },
    "value": {
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
