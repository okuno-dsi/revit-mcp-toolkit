# list_area_schemes

- Category: Area
- Purpose: List all AreaSchemes in the active Revit document, optionally including Area counts.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It returns basic information about every `AreaScheme` so that you can reference them from other Area-related commands.

## Usage
- Method: list_area_schemes

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| includeCounts | bool | no | false |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "list_area_schemes",
  "params": {
    "includeCounts": true
  }
}
```

### Example Result (success)
```jsonc
{
  "ok": true,
  "areaSchemes": [
    { "id": 101, "name": "Gross Building", "areaCount": 42 },
    { "id": 102, "name": "Rentable",       "areaCount": 35 }
  ],
  "messages": [
    "2 AreaSchemes found."
  ]
}
```

## Related
- get_area_schemes
- get_areas_by_scheme

