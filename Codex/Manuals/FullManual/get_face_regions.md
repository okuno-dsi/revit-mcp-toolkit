# get_face_regions

- Category: ElementOps
- Purpose: Get Face Regions in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_face_regions

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | int | no/depends | 0 |
| faceIndex | unknown | no/depends |  |
| includeGeometry | bool | no/depends | false |
| includeMesh | bool | no/depends | false |
| includeRoom | bool | no/depends | true |
| includeSpace | bool | no/depends | false |
| maxPointsPerLoop | int | no/depends | 500 |
| probeCount | int | no/depends | 5 |
| probeOffsetMm | number | no/depends | 5.0 |
| probeStrategy | string | no/depends | cross |
| regionLimit | int | no/depends | 50 |
| regionOffset | int | no/depends | 0 |
| returnProbeHits | bool | no/depends | false |
| simplifyToleranceMm | number | no/depends | 20.0 |
| summaryOnly | bool | no/depends | false |
| tessellateChordMm | number | no/depends | 100.0 |
| uniqueId | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_face_regions",
  "params": {
    "elementId": 0,
    "faceIndex": "...",
    "includeGeometry": false,
    "includeMesh": false,
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
    "summaryOnly": false,
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
    "includeMesh": {
      "type": "boolean"
    },
    "simplifyToleranceMm": {
      "type": "number"
    },
    "summaryOnly": {
      "type": "boolean"
    },
    "includeRoom": {
      "type": "boolean"
    },
    "elementId": {
      "type": "integer"
    },
    "returnProbeHits": {
      "type": "boolean"
    },
    "probeOffsetMm": {
      "type": "number"
    },
    "regionOffset": {
      "type": "integer"
    },
    "maxPointsPerLoop": {
      "type": "integer"
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
    "includeGeometry": {
      "type": "boolean"
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
