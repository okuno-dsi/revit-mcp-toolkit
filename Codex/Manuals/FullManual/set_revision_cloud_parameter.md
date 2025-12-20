# set_revision_cloud_parameter

- Category: RevisionCloud
- Purpose: リビジョンクラウドのインスタンスパラメータ取得・設定

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: set_revision_cloud_parameter

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | int | no/depends |  |
| paramName | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_revision_cloud_parameter",
  "params": {
    "elementId": 0,
    "paramName": "..."
  }
}
```

## Related
- create_revision_cloud
- create_default_revision
- create_revision_circle
- move_revision_cloud
- delete_revision_cloud
- update_revision
- get_revision_cloud_types
- get_revision_cloud_type_parameters

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "paramName": {
      "type": "string"
    },
    "elementId": {
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
