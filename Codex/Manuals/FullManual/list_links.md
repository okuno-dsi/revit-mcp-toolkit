# list_links

- Category: LinkOps
- Purpose: List Links in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: list_links
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "list_links",
  "params": {}
}
```

### Response Fields (indicative)
- links

## Related
- reload_link
- unload_link
- reload_link_from
- bind_link
- detach_link

### Params Schema
```json
{
  "type": "object",
  "properties": {}
}
```

### Result Schema
```json
{
  "type": "object",
  "properties": {
    "links": {
      "type": [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "array",
        "null"
      ]
    }
  },
  "additionalProperties": true
}
```
