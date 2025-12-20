# lookup_element

- Category: Misc
- Purpose: Inspect a single element in a RevitLookup-style JSON form (identity, location, parameters, geometry summary, relations).

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It returns a detailed JSON representation of a single element, similar to what the RevitLookup add-in shows, but in a machine-readable format suitable for AI agents and external tools.

It is **read-only** and intended as a safe introspection primitive that other editing commands can rely on.

## Usage
- Method: `lookup_element`

### Parameters
| Name             | Type   | Required           | Default |
|------------------|--------|--------------------|---------|
| elementId        | int    | no / one of        | 0       |
| uniqueId         | string | no / one of        |         |
| includeGeometry  | bool   | no                 | true    |
| includeRelations | bool   | no                 | true    |

- At least one of `elementId` or `uniqueId` must be provided.
- If both are provided, `uniqueId` takes precedence.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "method": "lookup_element",
  "params": {
    "elementId": 6798116,
    "includeGeometry": true,
    "includeRelations": true
  }
}
```

### Example Result (shape)
```json
{
  "ok": true,
  "element": {
    "id": 6798116,
    "uniqueId": "8c0a...-0067bb24",
    "category": "Walls",
    "categoryId": -2000011,
    "familyName": "Basic Wall",
    "typeName": "(Some)W3",
    "isElementType": false,
    "level": { "id": 1147622, "name": "3FL" },
    "workset": { "id": 0, "name": "Workset1" },
    "designOption": { "id": null, "name": null },
    "location": {
      "kind": "LocationCurve",
      "curveType": "Line",
      "start": { "x": 87.59, "y": 64.42, "z": 26.90 },
      "end":   { "x": 92.52, "y": 64.42, "z": 26.90 }
    },
    "boundingBox": {
      "min": { "x": 87.40, "y": 64.22, "z": 26.90 },
      "max": { "x": 92.52, "y": 64.62, "z": 39.53 }
    },
    "geometrySummary": {
      "hasSolid": true,
      "solidCount": 1,
      "approxVolume": 0.686,
      "approxSurfaceArea": 12.715
    },
    "parameters": [
      {
        "name": "Type Name",
        "builtin": "SYMBOL_NAME_PARAM",
        "storageType": "String",
        "parameterGroup": "PG_IDENTITY_DATA",
        "parameterGroupLabel": "Identity Data",
        "isReadOnly": true,
        "isShared": false,
        "isInstance": false,
        "guid": null,
        "value": "RC150",
        "displayValue": "RC150"
      }
      // ... more parameters ...
    ],
    "relations": {
      "hostId": null,
      "superComponentId": null,
      "groupId": null
    }
  }
}
```

## Notes
- Geometry summary is lightweight: only coarse solids and aggregate volume/area are reported; full tessellated geometry is not included.
- `parameters` includes both instance and type parameters, and both project and shared parameters when present.
- `relations` currently reports host, super-component, and group membership; additional relation fields may be added in future versions.

## Related
- get_element_info
- get_selected_element_ids
