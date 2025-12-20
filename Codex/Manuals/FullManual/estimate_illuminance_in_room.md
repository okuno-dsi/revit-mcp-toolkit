# estimate_illuminance_in_room

- Category: LightingOps
- Purpose: Estimate Illuminance In Room in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: estimate_illuminance_in_room
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "estimate_illuminance_in_room",
  "params": {}
}
```

## Related
- list_lighting_fixtures
- get_lighting_power_summary
- check_lighting_energy
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
