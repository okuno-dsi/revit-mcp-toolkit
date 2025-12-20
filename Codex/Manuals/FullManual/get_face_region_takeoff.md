# get_face_region_takeoff

- Category: ElementOps
- Purpose: Get Face Region Takeoff in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_face_region_takeoff

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | int | no/depends | 0 |
| faceIndex | unknown | no/depends |  |
| includeLoops | bool | no/depends | false |
| includeRoom | bool | no/depends | true |
| includeSpace | bool | no/depends | false |
| maxPointsPerLoop | int | no/depends | 300 |
| probeCount | int | no/depends | 5 |
| probeOffsetMm | number | no/depends | 5.0 |
| probeStrategy | string | no/depends | cross |
| regionLimit | int | no/depends | 50 |
| regionOffset | int | no/depends | 0 |
| returnProbeHits | bool | no/depends | false |
| simplifyToleranceMm | number | no/depends | 20.0 |
| tessellateChordMm | number | no/depends | 100.0 |
| uniqueId | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_face_region_takeoff",
  "params": {
    "elementId": 0,
    "faceIndex": "...",
    "includeLoops": false,
    "includeRoom": false,
    "includeSpace": false,
    "maxPointsPerLoop": 0,
    "probeCount": 0,
    "probeOffsetMm": 0.0,
    "probeStrategy": "...",
    "regionLimit": 0,
    "regionOffset": 0,
    "returnProbeHits": false,
    "simplifyToleranceMm": 0.0,
    "tessellateChordMm": 0.0,
    "uniqueId": "..."
  }
}
```

## Related
- get_materials
- get_material_parameters
- list_material_parameters
- update_material_parameter
- duplicate_material
- rename_material
- delete_material
- create_material

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "probeStrategy": {
      "type": "string"
    },
    "returnProbeHits": {
      "type": "boolean"
    },
    "simplifyToleranceMm": {
      "type": "number"
    },
    "includeRoom": {
      "type": "boolean"
    },
    "elementId": {
      "type": "integer"
    },
    "regionOffset": {
      "type": "integer"
    },
    "probeOffsetMm": {
      "type": "number"
    },
    "includeLoops": {
      "type": "boolean"
    },
    "tessellateChordMm": {
      "type": "number"
    },
    "regionLimit": {
      "type": "integer"
    },
    "faceIndex": {
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
    "probeCount": {
      "type": "integer"
    },
    "uniqueId": {
      "type": "string"
    },
    "maxPointsPerLoop": {
      "type": "integer"
    },
    "includeSpace": {
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
