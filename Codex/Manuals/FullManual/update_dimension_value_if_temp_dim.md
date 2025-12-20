# update_dimension_value_if_temp_dim

- Category: ConstraintOps
- Purpose: Constraint ops (lock/unlock/alignment/update via dimension)

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: update_dimension_value_if_temp_dim

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| {key} | unknown | yes |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_dimension_value_if_temp_dim",
  "params": {
    "{key}": "..."
  }
}
```

## Related
- lock_constraint
- unlock_constraint
- set_alignment_constraint

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "{key}": {
      "type": [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "array",
        "null"
      ]
    }
  },
  "required": [
    "{key}"
  ]
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
