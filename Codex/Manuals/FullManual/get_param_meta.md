# get_param_meta

- Category: ParamOps
- Purpose: Get Param Meta in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_param_meta

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| maxCount | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_param_meta",
  "params": {
    "maxCount": 0
  }
}
```

## Related
- get_param_values
- get_parameter_identity
- get_type_parameters_bulk
- get_instance_parameters_bulk
- update_parameters_batch

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "maxCount": {
      "type": "integer"
    }
  }
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
