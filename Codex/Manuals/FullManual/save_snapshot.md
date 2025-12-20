# save_snapshot

- Category: DocumentOps
- Purpose: Keep current doc as-is, save a timestamped snapshot alongside.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: save_snapshot

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| autoTimestamp | bool | no/depends | true |
| baseName | string | no/depends |  |
| dir | string | no/depends |  |
| prefix | string | no/depends |  |
| timestampFormat | string | no/depends | yyyyMMddHHmmss |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "save_snapshot",
  "params": {
    "autoTimestamp": false,
    "baseName": "...",
    "dir": "...",
    "prefix": "...",
    "timestampFormat": "..."
  }
}
```

## Related
- get_project_info
- get_project_categories
- get_project_summary

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "baseName": {
      "type": "string"
    },
    "dir": {
      "type": "string"
    },
    "timestampFormat": {
      "type": "string"
    },
    "autoTimestamp": {
      "type": "boolean"
    },
    "prefix": {
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
