# create_revision_circle

- Category: RevisionCloud
- Purpose: Create Revision Circle in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_revision_circle

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| radiusMm | number | no/depends | 0.0 |
| revisionId | int | no/depends | 0 |
| segments | int | no/depends | 24 |
| viewId | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_revision_circle",
  "params": {
    "radiusMm": 0.0,
    "revisionId": 0,
    "segments": 0,
    "viewId": 0
  }
}
```

## Related
- create_revision_cloud
- create_default_revision
- move_revision_cloud
- delete_revision_cloud
- update_revision
- get_revision_cloud_types
- get_revision_cloud_type_parameters
- set_revision_cloud_type_parameter

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "radiusMm": {
      "type": "number"
    },
    "segments": {
      "type": "integer"
    },
    "viewId": {
      "type": "integer"
    },
    "revisionId": {
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
