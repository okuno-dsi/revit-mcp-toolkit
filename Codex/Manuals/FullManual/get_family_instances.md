# get_family_instances

- Category: ElementOps
- Purpose: Get Family Instances in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_family_instances

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| categoryIds | unknown | no/depends |  |
| categoryNames | unknown | no/depends |  |
| count | int | no/depends |  |
| elementId | int | no/depends | 0 |
| nameContains | string | no/depends |  |
| namesOnly | bool | no/depends | false |
| skip | int | no/depends | 0 |
| summaryOnly | bool | no/depends | false |
| uniqueId | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_family_instances",
  "params": {
    "categoryIds": "...",
    "categoryNames": "...",
    "count": 0,
    "elementId": 0,
    "nameContains": "...",
    "namesOnly": false,
    "skip": 0,
    "summaryOnly": false,
    "uniqueId": "..."
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
    "nameContains": {
      "type": "string"
    },
    "categoryNames": {
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
    "summaryOnly": {
      "type": "boolean"
    },
    "uniqueId": {
      "type": "string"
    },
    "skip": {
      "type": "integer"
    },
    "categoryIds": {
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
