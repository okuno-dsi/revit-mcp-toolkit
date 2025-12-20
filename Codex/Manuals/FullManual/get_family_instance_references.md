# get_family_instance_references

- Category: ElementOps / FamilyInstanceOps
- Purpose: List `FamilyInstanceReferenceType` references (stable representations) available on a `FamilyInstance`.

## Overview
Advanced dimensioning (e.g., glass/louver/knob inside a door) requires the exact `Autodesk.Revit.DB.Reference` handles exposed by the family.

This command enumerates `FamilyInstance.GetReferences(FamilyInstanceReferenceType)` and returns each reference as a **stable representation** string, which can be fed into other commands (e.g. `add_door_size_dimensions.dimensionSpecs`).

## Usage
- Method: `get_family_instance_references`

### Parameters
```jsonc
{
  "elementId": 0,                    // optional (if omitted, uses the single selected element)
  "uniqueId": null,                  // optional
  "referenceTypes": ["Left","Right"],// optional filter (enum names, case-insensitive)
  "includeStable": true,             // optional (default: true)
  "includeGeometry": false,          // optional (default: false) best-effort geometry description
  "includeEmpty": false,             // optional (default: false)
  "maxPerType": 50                   // optional (default: 50) limit per reference type
}
```

### Example Request (selected element)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_family_instance_references",
  "params": {
    "includeStable": true,
    "includeEmpty": false
  }
}
```

## Notes
- If the selected element is not a `FamilyInstance`, the command returns `ok:false`.
- Many families expose only a small subset of reference planes; in that case most `refType` groups will have `count:0`.

