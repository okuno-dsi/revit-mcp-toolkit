# create_area_scheme

- Category: Area
- Purpose: (Not supported) Create Area Scheme in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

Important
- Revit 2024 API does not support creating a new `AreaScheme` programmatically in this add-in. This command returns `ok=false`.
- Create the Area Scheme in the Revit UI, then use `get_area_schemes` to obtain its `schemeId`.

## Usage
- Method: create_area_scheme
- Parameters: none (ignored)

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_area_scheme",
  "params": {}
}
```

### Example Result (Current Behavior)
```json
{
  "ok": false,
  "msg": "AreaScheme の作成は、このRevitバージョン/APIではサポートされていません（UIで作成後、get_area_schemesで取得してください）。"
}
```

## Related
- get_area_schemes
- get_area_boundary
- create_area
- get_areas
- get_area_params
- update_area
- move_area
- delete_area
- get_area_boundary_walls

### Params Schema
```json
{
  "type": "object",
  "properties": {}
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
