# get_param_values

- Category: ParamOps
- Purpose: JSON-RPC "get_param_values" をピンポイント高速実装

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_param_values

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| includeMeta | bool | no/depends | true |
| mode | string | no/depends | element |
| scope | string | no/depends | auto |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_param_values",
  "params": {
    "includeMeta": false,
    "mode": "...",
    "scope": "..."
  }
}
```

## Related
- get_param_meta
- get_parameter_identity
- get_type_parameters_bulk
- get_instance_parameters_bulk
- update_parameters_batch

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "mode": {
      "type": "string"
    },
    "includeMeta": {
      "type": "boolean"
    },
    "scope": {
      "type": "string"
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
