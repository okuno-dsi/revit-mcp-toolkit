# element.copy_elements

- Category: ElementOps
- Purpose: Copy elements by translation vector within the same document.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: element.copy_elements
- Aliases: copy_elements, element.copy

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementIds | int[] | yes |  |
| translation | object | yes |  |
| units | string | no | "mm" |
| lengthUnits | string | no | "mm" |
| options | object | no |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "element.copy_elements",
  "params": {
    "elementIds": [123],
    "translation": { "x": 1000, "y": 0, "z": 0 },
    "units": "mm",
    "options": { "failIfPinned": true }
  }
}
```

## Related
- element.mirror_elements
- element.array_linear
- element.array_radial
- element.pin_element
- element.pin_elements

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "elementIds": {
      "type": "array",
      "items": { "type": "integer" }
    },
    "translation": {
      "type": "object",
      "properties": {
        "x": { "type": "number" },
        "y": { "type": "number" },
        "z": { "type": "number" }
      }
    },
    "units": { "type": "string" },
    "lengthUnits": { "type": "string" },
    "options": {
      "type": "object",
      "properties": {
        "failIfPinned": { "type": "boolean" }
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
