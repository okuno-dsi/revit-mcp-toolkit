# diff_elements

- Category: AnalysisOps
- Purpose: Diff Elements in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: diff_elements

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| endpointsTolMm | number | no/depends | 30.0 |
| includeEndpoints | bool | no/depends | true |
| posTolMm | number | no/depends | 600.0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "diff_elements",
  "params": {
    "endpointsTolMm": 0.0,
    "includeEndpoints": false,
    "posTolMm": 0.0
  }
}
```

## Related
- summarize_elements_by_category
- summarize_family_types_by_category
- check_clashes

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "includeEndpoints": {
      "type": "boolean"
    },
    "endpointsTolMm": {
      "type": "number"
    },
    "posTolMm": {
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
