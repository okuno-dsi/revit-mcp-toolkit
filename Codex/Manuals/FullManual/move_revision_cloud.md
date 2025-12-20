# move_revision_cloud

- Category: RevisionCloud
- Purpose: Move Revision Cloud in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: move_revision_cloud

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| dx | number | no/depends |  |
| dy | number | no/depends |  |
| dz | number | no/depends |  |
| elementId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "move_revision_cloud",
  "params": {
    "dx": 0.0,
    "dy": 0.0,
    "dz": 0.0,
    "elementId": 0
  }
}
```

## Related
- create_revision_cloud
- create_default_revision
- create_revision_circle
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
    "dz": {
      "type": "number"
    },
    "elementId": {
      "type": "integer"
    },
    "dy": {
      "type": "number"
    },
    "dx": {
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
