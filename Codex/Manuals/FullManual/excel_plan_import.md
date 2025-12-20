# excel_plan_import

- Category: ExcelPlan
- Purpose: Excel Plan Import in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: excel_plan_import

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| baseOffsetMm | number | no/depends | 0.0 |
| cellSizeMeters | number | no/depends | 1.0 |
| debugSheetName | string | no/depends | Recreated |
| debugWriteBack | bool | no/depends | false |
| ensurePerimeter | bool | no/depends | false |
| excelPath | string | no/depends |  |
| exportOnly | bool | no/depends | false |
| flip | bool | no/depends | false |
| heightMm | number | no/depends |  |
| levelName | string | no/depends |  |
| mode | string | no/depends | Walls |
| placeGrids | bool | no/depends | false |
| placeRooms | bool | no/depends | false |
| roomLevelName | string | no/depends |  |
| roomPhaseName | string | no/depends |  |
| serviceUrl | string | no/depends |  |
| setRoomNameFromLabel | bool | no/depends | true |
| sheetName | string | no/depends |  |
| toleranceCell | number | no/depends | 0.001 |
| topLevelName | string | no/depends |  |
| useColorMask | bool | no/depends | true |
| wallTypeName | string | no/depends | RC150 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "excel_plan_import",
  "params": {
    "baseOffsetMm": 0.0,
    "cellSizeMeters": 0.0,
    "debugSheetName": "...",
    "debugWriteBack": false,
    "ensurePerimeter": false,
    "excelPath": "...",
    "exportOnly": false,
    "flip": false,
    "heightMm": 0.0,
    "levelName": "...",
    "mode": "...",
    "placeGrids": false,
    "placeRooms": false,
    "roomLevelName": "...",
    "roomPhaseName": "...",
    "serviceUrl": "...",
    "setRoomNameFromLabel": false,
    "sheetName": "...",
    "toleranceCell": 0.0,
    "topLevelName": "...",
    "useColorMask": false,
    "wallTypeName": "..."
  }
}
```

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "useColorMask": {
      "type": "boolean"
    },
    "ensurePerimeter": {
      "type": "boolean"
    },
    "wallTypeName": {
      "type": "string"
    },
    "excelPath": {
      "type": "string"
    },
    "flip": {
      "type": "boolean"
    },
    "placeGrids": {
      "type": "boolean"
    },
    "roomLevelName": {
      "type": "string"
    },
    "debugSheetName": {
      "type": "string"
    },
    "sheetName": {
      "type": "string"
    },
    "toleranceCell": {
      "type": "number"
    },
    "levelName": {
      "type": "string"
    },
    "cellSizeMeters": {
      "type": "number"
    },
    "heightMm": {
      "type": "number"
    },
    "roomPhaseName": {
      "type": "string"
    },
    "exportOnly": {
      "type": "boolean"
    },
    "placeRooms": {
      "type": "boolean"
    },
    "mode": {
      "type": "string"
    },
    "setRoomNameFromLabel": {
      "type": "boolean"
    },
    "baseOffsetMm": {
      "type": "number"
    },
    "topLevelName": {
      "type": "string"
    },
    "serviceUrl": {
      "type": "string"
    },
    "debugWriteBack": {
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
