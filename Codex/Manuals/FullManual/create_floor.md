# create_floor

- Category: ElementOps
- Purpose: 柔軟 boundary 入力と親切エラー、Floor.Create を使用

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_floor

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| floorTypeName | string | no/depends |  |
| isStructural | bool | no/depends | false |
| levelName | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_floor",
  "params": {
    "floorTypeName": "...",
    "isStructural": false,
    "levelName": "..."
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
    "levelName": {
      "type": "string"
    },
    "floorTypeName": {
      "type": "string"
    },
    "isStructural": {
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
