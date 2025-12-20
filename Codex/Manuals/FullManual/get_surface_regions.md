# get_surface_regions

- Category: SurfaceOps
- Purpose: Get Surface Regions in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_surface_regions

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| dir | string | no/depends |  |
| hostKind | string | no/depends | auto |
| includePainted | bool | no/depends | true |
| includeUnpainted | bool | no/depends | true |
| side | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_surface_regions",
  "params": {
    "dir": "...",
    "hostKind": "...",
    "includePainted": false,
    "includeUnpainted": false,
    "side": "..."
  }
}
```

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "side": {
      "type": "string"
    },
    "dir": {
      "type": "string"
    },
    "hostKind": {
      "type": "string"
    },
    "includeUnpainted": {
      "type": "boolean"
    },
    "includePainted": {
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
