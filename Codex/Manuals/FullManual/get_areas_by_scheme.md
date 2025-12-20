# get_areas_by_scheme

- Category: Area
- Purpose: List Areas for a given AreaScheme, optionally filtered by level and enriched with selected parameters.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It resolves an `AreaScheme` by ID or name, then returns the Areas that belong to that scheme (optionally restricted to specific levels).

## Usage
- Method: get_areas_by_scheme

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| schemeId | int | yes\* |  |
| schemeName | string | yes\* |  |
| levelNames | string[] | no |  |
| includeParameters | string[] | no |  |

\* Either `schemeId` or `schemeName` is required. If both are given, `schemeId` takes precedence.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_areas_by_scheme",
  "params": {
    "schemeName": "Rentable",
    "levelNames": ["Level 2"],
    "includeParameters": ["Department", "Comments"]
  }
}
```

### Example Result (success)
```jsonc
{
  "ok": true,
  "scheme": {
    "id": 102,
    "name": "Rentable"
  },
  "areas": [
    {
      "id": 5001,
      "number": "AR-201",
      "name": "Tenant A",
      "levelName": "Level 2",
      "area": 123.45,
      "unit": "m2",
      "extraParams": {
        "Department": {
          "name": "Department",
          "value": "Sales",
          "display": "Sales"
        },
        "Comments": {
          "name": "Comments",
          "value": "Key tenant",
          "display": "Key tenant"
        }
      }
    }
  ],
  "messages": [
    "AreaScheme 'Rentable' (id=102) resolved.",
    "1 Areas returned for requested levels."
  ]
}
```

If there are no matching Areas on the requested levels, `areas` is an empty array and `ok` remains `true`; `messages` explains that the result is empty.

## Related
- list_area_schemes
- get_areas
- get_area_params

