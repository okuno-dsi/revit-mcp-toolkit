# get_oriented_bbox

- Category: GetOrientedBoundingBoxHandler.cs
- Purpose: JSON-RPC "get_oriented_bbox" の実体（返却を mm へ統一）

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_oriented_bbox

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| detailLevel | string | no/depends | fine |
| elementId | unknown | no/depends |  |
| includeCorners | bool | no/depends | true |
| strategy | string | no/depends | auto |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_oriented_bbox",
  "params": {
    "detailLevel": "...",
    "elementId": "...",
    "includeCorners": false,
    "strategy": "..."
  }
}
```

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "strategy": {
      "type": "string"
    },
    "elementId": {
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
    "includeCorners": {
      "type": "boolean"
    },
    "detailLevel": {
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
