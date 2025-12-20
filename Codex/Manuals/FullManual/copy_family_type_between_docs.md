# copy_family_type_between_docs

- Category: ElementOps
- Purpose: Copy Family Type Between Docs in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: copy_family_type_between_docs
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "copy_family_type_between_docs",
  "params": {}
}
```

## Related
- get_materials
- get_material_parameters
- list_material_parameters
- update_material_parameter
- duplicate_material
- rename_material
- delete_material
- create_material

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
