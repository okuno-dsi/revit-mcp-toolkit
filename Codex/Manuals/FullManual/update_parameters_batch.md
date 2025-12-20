# update_parameters_batch

- Category: ParamOps
- Purpose: Update Parameters Batch in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: update_parameters_batch

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| batchSize | int | no/depends |  |
| maxMillisPerTx | int | no/depends | 2500 |
| startIndex | int | no/depends | 0 |
| suppressItems | bool | no/depends | false |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_parameters_batch",
  "params": {
    "batchSize": 0,
    "maxMillisPerTx": 0,
    "startIndex": 0,
    "suppressItems": false
  }
}
```

## Related
- get_param_values
- get_param_meta
- get_parameter_identity
- get_type_parameters_bulk
- get_instance_parameters_bulk

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "maxMillisPerTx": {
      "type": "integer"
    },
    "startIndex": {
      "type": "integer"
    },
    "batchSize": {
      "type": "integer"
    },
    "suppressItems": {
      "type": "boolean"
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
