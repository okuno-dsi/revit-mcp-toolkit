# get_elements_in_view

- Category: GeneralOps
- Purpose: Get Elements In View in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_elements_in_view

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| categoryNameContains | string | no/depends |  |
| excludeImports | bool | no/depends | true |
| familyNameContains | string | no/depends |  |
| grouped | unknown | no/depends |  |
| includeElementTypes | bool | no/depends | false |
| includeIndependentTags | bool | no/depends | true |
| levelId | int | no/depends |  |
| levelName | string | no/depends |  |
| modelOnly | bool | no/depends | false |
| summaryOnly | bool | no/depends | false |
| typeNameContains | string | no/depends |  |
| viewId | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_elements_in_view",
  "params": {
    "categoryNameContains": "...",
    "excludeImports": false,
    "familyNameContains": "...",
    "grouped": "...",
    "includeElementTypes": false,
    "includeIndependentTags": false,
    "levelId": 0,
    "levelName": "...",
    "modelOnly": false,
    "summaryOnly": false,
    "typeNameContains": "...",
    "viewId": "..."
  }
}
```

## Related
- get_types_in_view
- select_elements_by_filter_id
- select_elements

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "familyNameContains": {
      "type": "string"
    },
    "typeNameContains": {
      "type": "string"
    },
    "modelOnly": {
      "type": "boolean"
    },
    "categoryNameContains": {
      "type": "string"
    },
    "excludeImports": {
      "type": "boolean"
    },
    "summaryOnly": {
      "type": "boolean"
    },
    "includeIndependentTags": {
      "type": "boolean"
    },
    "includeElementTypes": {
      "type": "boolean"
    },
    "grouped": {
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
    "viewId": {
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
      "type": "string"
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
