# create_curtain_wall

- Category: ElementOps
- Purpose: Create Curtain Wall in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_curtain_wall

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| baseLevelId | unknown | no/depends |  |
| baseLevelName | unknown | no/depends |  |
| baseOffsetMm | number | no/depends | 0.0 |
| levelId | unknown | no/depends |  |
| levelName | unknown | no/depends |  |
| topLevelId | unknown | no/depends |  |
| topLevelName | unknown | no/depends |  |
| topOffsetMm | number | no/depends | 0.0 |
| typeId | unknown | no/depends |  |
| typeName | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_curtain_wall",
  "params": {
    "baseLevelId": "...",
    "baseLevelName": "...",
    "baseOffsetMm": 0.0,
    "levelId": "...",
    "levelName": "...",
    "topLevelId": "...",
    "topLevelName": "...",
    "topOffsetMm": 0.0,
    "typeId": "...",
    "typeName": "..."
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
    "topLevelName": {
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
    "topLevelId": {
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
    "baseOffsetMm": {
      "type": "number"
    },
    "typeId": {
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
    "baseLevelName": {
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
    "levelName": {
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
    "baseLevelId": {
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
    "typeName": {
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
    "topOffsetMm": {
      "type": "number"
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
