# get_revision_cloud_parameters

- Category: RevisionCloud
- Purpose: リビジョンクラウドのインスタンスパラメータ取得・設定

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_revision_cloud_parameters

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_revision_cloud_parameters",
  "params": {
    "elementId": 0
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
