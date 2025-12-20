# export_snapshot_bundle

- Category: Revision
- Purpose: 複合スナップショット（elements + levels + grids + layers + materials ...）

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: export_snapshot_bundle
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "export_snapshot_bundle",
  "params": {}
}
```

## Related
- export_snapshot

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
