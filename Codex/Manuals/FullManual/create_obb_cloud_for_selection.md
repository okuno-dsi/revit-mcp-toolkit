# create_obb_cloud_for_selection

- Category: RevisionCloud
- Purpose: Create Obb Cloud For Selection in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_obb_cloud_for_selection

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| ensureCloudVisible | bool | no/depends | true |
| paddingMm | number | no/depends |  |
| widthMm | number | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_obb_cloud_for_selection",
  "params": {
    "ensureCloudVisible": false,
    "paddingMm": 0.0,
    "widthMm": 0.0
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
    "widthMm": {
      "type": "number"
    },
    "ensureCloudVisible": {
      "type": "boolean"
    },
    "paddingMm": {
      "type": "number"
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
