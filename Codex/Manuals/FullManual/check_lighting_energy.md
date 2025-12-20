# check_lighting_energy

- Category: LightingOps
- Purpose: Check Lighting Energy in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: check_lighting_energy
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "check_lighting_energy",
  "params": {}
}
```

## Related
- list_lighting_fixtures
- get_lighting_power_summary
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
