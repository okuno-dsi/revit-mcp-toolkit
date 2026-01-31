# element.pin_element

- Category: ElementOps
- Purpose: Pin or unpin a single element.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: element.pin_element
- Aliases: pin_element

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | int | yes |  |
| pinned | bool | no | true |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "element.pin_element",
  "params": {
    "elementId": 123,
    "pinned": true
  }
}
```

## Related
- element.pin_elements
- element.unpin_element
- element.unpin_elements

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "elementId": { "type": "integer" },
    "pinned": { "type": "boolean" }
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
