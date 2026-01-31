# element.array_radial

- Category: ElementOps
- Purpose: Create a radial array of elements (associative or non-associative).

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: element.array_radial
- Aliases: array_radial

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementIds | int[] | yes |  |
| count | int | yes |  |
| axis | object | yes |  |
| angle | number | yes |  |
| units | string | no | "mm" |
| lengthUnits | string | no | "mm" |
| angleUnits | string | no | "rad" |
| anchorMember | string | no | "Last" |
| associate | bool | no | true |
| viewId | int | no | (active view) |
| options | object | no |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "element.array_radial",
  "params": {
    "elementIds": [123],
    "count": 6,
    "axis": { "p0": { "x": 0, "y": 0, "z": 0 }, "p1": { "x": 0, "y": 0, "z": 1 } },
    "angle": 90,
    "angleUnits": "deg",
    "units": "mm",
    "anchorMember": "Last",
    "associate": true
  }
}
```

## Related
- element.array_linear
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
    "axis": {
      "type": "object",
      "properties": {
        "p0": { "type": "object" },
        "p1": { "type": "object" }
      }
    },
    "angle": { "type": "number" },
    "units": { "type": "string" },
    "lengthUnits": { "type": "string" },
    "angleUnits": { "type": "string" },
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
