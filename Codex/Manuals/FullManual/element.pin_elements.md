# element.pin_elements

- Category: ElementOps
- Purpose: Pin or unpin multiple elements (optionally continue on error).

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: element.pin_elements
- Aliases: pin_elements

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementIds | int[] | yes |  |
| pinned | bool | no | true |
| options | object | no |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "element.pin_elements",
  "params": {
    "elementIds": [123, 456],
    "pinned": true,
    "options": { "continueOnError": true }
  }
}
```

## Related
- element.pin_element
- element.unpin_element
- element.unpin_elements

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "elementIds": {
      "type": "array",
      "items": { "type": "integer" }
    },
    "pinned": { "type": "boolean" },
    "options": {
      "type": "object",
      "properties": {
        "continueOnError": { "type": "boolean" }
      }
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
