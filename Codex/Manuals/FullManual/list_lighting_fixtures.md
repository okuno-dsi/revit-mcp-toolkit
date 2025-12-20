# list_lighting_fixtures

- Category: LightingOps
- Purpose: List Lighting Fixtures in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: list_lighting_fixtures
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "list_lighting_fixtures",
  "params": {}
}
```

## Related
- get_lighting_power_summary
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
