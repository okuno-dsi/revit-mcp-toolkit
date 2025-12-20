# analyze_segments

- Category: Commands
- Purpose: Analyze Segments in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: analyze_segments

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| mode | string | no/depends | 2d |
| point | object | no/depends |  |
| seg1 | object | no/depends |  |
| seg2 | object | no/depends |  |
| tol | object | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "analyze_segments",
  "params": {
    "mode": "...",
    "point": "...",
    "seg1": "...",
    "seg2": "...",
    "tol": "..."
  }
}
```

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "point": {
      "type": "object"
    },
    "seg1": {
      "type": "object"
    },
    "tol": {
      "type": "object"
    },
    "seg2": {
      "type": "object"
    },
    "mode": {
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
