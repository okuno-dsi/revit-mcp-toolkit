# get_types_in_view

- Category: GeneralOps
- Purpose: Get Types In View in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_types_in_view

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| includeCounts | bool | no/depends | true |
| includeElementTypes | bool | no/depends | false |
| includeIndependentTags | bool | no/depends | false |
| includeTypeInfo | bool | no/depends | true |
| modelOnly | bool | no/depends | true |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_types_in_view",
  "params": {
    "includeCounts": false,
    "includeElementTypes": false,
    "includeIndependentTags": false,
    "includeTypeInfo": false,
    "modelOnly": false
  }
}
```

## Related
- get_elements_in_view
- select_elements_by_filter_id
- select_elements

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "includeIndependentTags": {
      "type": "boolean"
    },
    "includeCounts": {
      "type": "boolean"
    },
    "includeTypeInfo": {
      "type": "boolean"
    },
    "includeElementTypes": {
      "type": "boolean"
    },
    "modelOnly": {
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
