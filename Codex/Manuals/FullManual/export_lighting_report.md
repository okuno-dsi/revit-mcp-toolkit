# export_lighting_report

- Category: LightingOps
- Purpose: Export Lighting Report in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: export_lighting_report
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "export_lighting_report",
  "params": {}
}
```

## Related
- list_lighting_fixtures
- get_lighting_power_summary
- check_lighting_energy
- estimate_illuminance_in_room

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
