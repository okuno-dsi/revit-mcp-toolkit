# create_wall

- Category: ElementOps
- Purpose: Create Wall in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_wall

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| baseLevelId | unknown | no/depends |  |
| baseLevelName | unknown | no/depends |  |
| baseOffsetMm | number | no/depends | 0.0 |
| isStructural | bool | no/depends | false |
| levelId | unknown | no/depends |  |
| levelName | unknown | no/depends |  |
| topLevelId | unknown | no/depends |  |
| topLevelName | unknown | no/depends |  |
| topOffsetMm | number | no/depends | 0.0 |
| wallTypeId | unknown | no/depends |  |
| wallTypeName | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_wall",
  "params": {
    "baseLevelId": "...",
    "baseLevelName": "...",
    "baseOffsetMm": 0.0,
    "isStructural": false,
    "levelId": "...",
    "levelName": "...",
    "topLevelId": "...",
    "topLevelName": "...",
    "topOffsetMm": 0.0,
    "wallTypeId": "...",
    "wallTypeName": "..."
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
    "wallTypeId": {
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
    "wallTypeName": {
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
    "baseOffsetMm": {
      "type": "number"
    },
    "isStructural": {
      "type": "boolean"
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
