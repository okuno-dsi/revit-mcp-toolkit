# get_lighting_power_summary

- Category: LightingOps
- Purpose: Get Lighting Power Summary in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_lighting_power_summary
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_lighting_power_summary",
  "params": {}
}
```

## Related
- list_lighting_fixtures
- check_lighting_energy
- estimate_illuminance_in_room
- export_lighting_report

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
