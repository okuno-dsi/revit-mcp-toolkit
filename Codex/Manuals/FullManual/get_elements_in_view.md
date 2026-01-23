# get_elements_in_view

- Category: GeneralOps
- Purpose: Get Elements In View in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

Notes:
- If `viewId` is omitted, the active view is used and `usedActiveView=true` is returned.
- `categoryNames` are resolved against known BuiltInCategory names when possible; unresolved names are matched by category display name.
- `count` / `skip` are accepted as top-level paging aliases (in addition to `_shape.page.limit/skip`).
- The response includes `items` as an alias of `rows` for convenience.

## Usage
- Method: get_elements_in_view

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| categoryNameContains | string | no/depends |  |
| categoryNames | string[] | no/depends |  |
| categoryIds | int[] | no/depends |  |
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
| elementIds | int[] | no/depends |  |
| count | int | no/depends |  |
| skip | int | no/depends |  |
| viewId | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_elements_in_view",
  "params": {
    "categoryNameContains": "...",
    "categoryNames": ["Walls", "Floors"],
    "categoryIds": [ -2000011 ],
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
    "elementIds": [ 123, 456 ],
    "count": 100,
    "skip": 0,
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
    "categoryNames": {
      "type": "array",
      "items": {
        "type": "string"
      }
    },
    "categoryIds": {
      "type": "array",
      "items": {
        "type": "integer"
      }
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
    "elementIds": {
      "type": "array",
      "items": {
        "type": "integer"
      }
    },
    "count": {
      "type": "integer"
    },
    "skip": {
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
