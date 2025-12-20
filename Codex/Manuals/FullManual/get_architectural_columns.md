# get_architectural_columns

- Category: ElementOps
- Purpose: Get Architectural Columns in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_architectural_columns

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| bbox | unknown | no/depends |  |
| count | int | no/depends |  |
| levelId | int | no/depends |  |
| skip | int | no/depends | 0 |
| sortBy | string | no/depends | id |
| summaryOnly | bool | no/depends | false |
| typeId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_architectural_columns",
  "params": {
    "bbox": "...",
    "count": 0,
    "levelId": 0,
    "skip": 0,
    "sortBy": "...",
    "summaryOnly": false,
    "typeId": 0
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
    "bbox": {
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
    },
    "skip": {
      "type": "integer"
    },
    "summaryOnly": {
      "type": "boolean"
    },
    "typeId": {
      "type": "integer"
    },
    "sortBy": {
      "type": "string"
    },
    "count": {
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
