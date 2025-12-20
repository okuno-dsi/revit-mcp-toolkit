# get_type_parameters_bulk

- Category: ParamOps
- Purpose: Get Type Parameters Bulk in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_type_parameters_bulk
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_type_parameters_bulk",
  "params": {}
}
```

## Related
- get_param_values
- get_param_meta
- get_parameter_identity
- get_instance_parameters_bulk
- update_parameters_batch

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
