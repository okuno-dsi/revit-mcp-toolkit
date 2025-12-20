# check_clashes

- Category: AnalysisOps
- Purpose: Clash detection - fast AABB(bbox) and precise Solid intersection

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: check_clashes

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| maxPairs | int | no/depends | 100000 |
| method | string | no/depends | bbox |
| minVolumeMm3 | number | no/depends | 0.0 |
| namesOnly | bool | no/depends | false |
| toleranceMm | number | no/depends | 0.0 |
| viewId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "check_clashes",
  "params": {
    "maxPairs": 0,
    "method": "...",
    "minVolumeMm3": 0.0,
    "namesOnly": false,
    "toleranceMm": 0.0,
    "viewId": 0
  }
}
```

## Related
- summarize_elements_by_category
- summarize_family_types_by_category
- diff_elements

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "maxPairs": {
      "type": "integer"
    },
    "minVolumeMm3": {
      "type": "number"
    },
    "namesOnly": {
      "type": "boolean"
    },
    "toleranceMm": {
      "type": "number"
    },
    "method": {
      "type": "string"
    },
    "viewId": {
      "type": "integer"
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
