# get_param_values

- Category: ParamOps
- Purpose: JSON-RPC "get_param_values" をピンポイント高速実装

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_param_values

### Parameters
| Name | Type | Required | Default | Description |
|---|---|---|---|---|
| includeMeta | bool | no | true | Include metadata such as spec and read-only flags |
| mode | string | no | element | `element` / `type` / `category` |
| scope | string | no | auto | `auto` / `instance` / `type` |
| docGuid | string | no |  | Target document `docGuid` / `docKey` |
| docTitle | string | no |  | Target document title |
| docPath | string | no |  | Target document full path |

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

### Notes
- `docGuid` / `docTitle` / `docPath` can be used to read from a non-active open document.
- The same document hints are also accepted via `meta.extensions`.
- `mode=element` requires `elementId`, `mode=type` requires `typeId`, and `mode=category` requires `category`.

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
    },
    "docGuid": {
      "type": "string"
    },
    "docTitle": {
      "type": "string"
    },
    "docPath": {
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
