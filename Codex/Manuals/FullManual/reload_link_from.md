# reload_link_from

- Category: LinkOps
- Purpose: Reload Link From in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: reload_link_from

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
|path| string |yes||
| worksetMode | string | no/depends | all |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "reload_link_from",
  "params": {
    "path": "...",
    "worksetMode": "..."
  }
}
```

## Related
- list_links
- reload_link
- unload_link
- bind_link
- detach_link

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "path": {
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
    "worksetMode": {
      "type": "string"
    }
  },
  "required": [
    "path"
  ]
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
