# compare_projects_summary

- Category: CompareOps
- Purpose: Compare Projects Summary in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: compare_projects_summary

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| baselineIndex | int | no/depends | 0 |
| endpointsTolMm | number | no/depends | 30.0 |
| includeEndpoints | bool | no/depends | true |
| ok | bool | no/depends |  |
| posTolMm | number | no/depends | 600.0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "compare_projects_summary",
  "params": {
    "baselineIndex": 0,
    "endpointsTolMm": 0.0,
    "includeEndpoints": false,
    "ok": false,
    "posTolMm": 0.0
  }
}
```

## Related
- validate_compare_context

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "includeEndpoints": {
      "type": "boolean"
    },
    "baselineIndex": {
      "type": "integer"
    },
    "ok": {
      "type": "boolean"
    },
    "posTolMm": {
      "type": "number"
    },
    "endpointsTolMm": {
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
