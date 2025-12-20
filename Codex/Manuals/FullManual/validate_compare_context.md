# validate_compare_context

- Category: CompareOps
- Purpose: Validate Compare Context in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: validate_compare_context

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| requireSameProject | bool | no/depends | true |
| requireSameViewType | bool | no/depends | true |
| resolveByName | bool | no/depends | true |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "validate_compare_context",
  "params": {
    "requireSameProject": false,
    "requireSameViewType": false,
    "resolveByName": false
  }
}
```

## Related
- compare_projects_summary

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "resolveByName": {
      "type": "boolean"
    },
    "requireSameProject": {
      "type": "boolean"
    },
    "requireSameViewType": {
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
