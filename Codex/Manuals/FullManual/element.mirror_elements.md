# element.mirror_elements

- Category: ElementOps
- Purpose: Mirror elements by plane (optionally keep originals or mirror in-place).

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: element.mirror_elements
- Aliases: mirror_elements, element.mirror

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementIds | int[] | yes |  |
| plane | object | yes |  |
| mirrorCopies | bool | no | true |
| units | string | no | "mm" |
| lengthUnits | string | no | "mm" |
| options | object | no |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "element.mirror_elements",
  "params": {
    "elementIds": [123, 456],
    "plane": {
      "origin": { "x": 0, "y": 0, "z": 0 },
      "normal": { "x": 1, "y": 0, "z": 0 }
    },
    "mirrorCopies": true,
    "units": "mm",
    "options": { "failIfPinned": true, "precheckCanMirror": true }
  }
}
```

## Related
- element.copy_elements
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
    "plane": {
      "type": "object",
      "properties": {
        "origin": { "type": "object" },
        "normal": { "type": "object" }
      }
    },
    "mirrorCopies": { "type": "boolean" },
    "units": { "type": "string" },
    "lengthUnits": { "type": "string" },
    "options": {
      "type": "object",
      "properties": {
        "failIfPinned": { "type": "boolean" },
        "precheckCanMirror": { "type": "boolean" }
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
