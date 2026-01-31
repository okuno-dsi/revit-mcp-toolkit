# element.array_linear

- Category: ElementOps
- Purpose: Create a linear array of elements (associative or non-associative).

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: element.array_linear
- Aliases: array_linear

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementIds | int[] | yes |  |
| count | int | yes |  |
| direction | object | yes |  |
| spacing | number | yes |  |
| units | string | no | "mm" |
| lengthUnits | string | no | "mm" |
| anchorMember | string | no | "Second" |
| associate | bool | no | true |
| viewId | int | no | (active view) |
| options | object | no |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "element.array_linear",
  "params": {
    "elementIds": [123],
    "count": 5,
    "direction": { "x": 1, "y": 0, "z": 0 },
    "spacing": 3000,
    "units": "mm",
    "anchorMember": "Second",
    "associate": true
  }
}
```

## Related
- element.array_radial
- element.copy_elements
- element.mirror_elements

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "elementIds": {
      "type": "array",
      "items": { "type": "integer" }
    },
    "count": { "type": "integer" },
    "direction": {
      "type": "object",
      "properties": {
        "x": { "type": "number" },
        "y": { "type": "number" },
        "z": { "type": "number" }
      }
    },
    "spacing": { "type": "number" },
    "units": { "type": "string" },
    "lengthUnits": { "type": "string" },
    "anchorMember": { "type": "string" },
    "associate": { "type": "boolean" },
    "viewId": { "type": "integer" },
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
