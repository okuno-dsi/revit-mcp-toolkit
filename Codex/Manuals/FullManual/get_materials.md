# get_materials

- Category: ElementOps
- Purpose: Get Materials in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_materials

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends |  |
| materialClass | string | no/depends |  |
| nameContains | string | no/depends |  |
| namesOnly | bool | no/depends | false |
| skip | int | no/depends | 0 |
| summaryOnly | bool | no/depends | false |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_materials",
  "params": {
    "count": 0,
    "materialClass": "...",
    "nameContains": "...",
    "namesOnly": false,
    "skip": 0,
    "summaryOnly": false
  }
}
```

## Related
- get_material_parameters
- list_material_parameters
- update_material_parameter
- duplicate_material
- rename_material
- delete_material
- create_material
- apply_material_to_element

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "nameContains": {
      "type": "string"
    },
    "skip": {
      "type": "integer"
    },
    "materialClass": {
      "type": "string"
    },
    "summaryOnly": {
      "type": "boolean"
    },
    "count": {
      "type": "integer"
    },
    "namesOnly": {
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
