# get_filled_region_types

- Category: Annotations/FilledRegion
- Purpose: List all FilledRegionType elements in the current document.

## Overview
Returns a list of `FilledRegionType` definitions, including pattern ids/names and foreground/background colors.  
Use this to discover available fill styles before creating or editing FilledRegion instances.

## Usage
- Method: get_filled_region_types
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_filled_region_types",
  "params": {}
}
```

## Result
```jsonc
{
  "ok": true,
  "totalCount": 5,
  "items": [
    {
      "typeId": 12345,
      "uniqueId": "....",
      "name": "Solid Black",
      "isMasking": false,
      "foregroundPatternId": 2001,
      "foregroundPatternName": "Solid fill",
      "foregroundColor": { "r": 0, "g": 0, "b": 0 },
      "backgroundPatternId": null,
      "backgroundPatternName": null,
      "backgroundColor": null
    }
  ]
}
```

## Related
- get_filled_regions_in_view
- create_filled_region
- set_filled_region_type

