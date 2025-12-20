# rename_types_by_parameter

- Category: TypeOps
- Purpose: Rename Types By Parameter in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: rename_types_by_parameter
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rename_types_by_parameter",
  "params": {}
}
```

## Related
- delete_type_if_unused
- purge_unused_types
- force_delete_type
- rename_types_bulk

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
