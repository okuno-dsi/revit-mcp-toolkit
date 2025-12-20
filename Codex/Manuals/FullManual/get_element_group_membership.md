# get_element_group_membership

- Category: GroupOps
- Purpose: Get Element Group Membership in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_element_group_membership

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_element_group_membership",
  "params": {
    "elementId": 0
  }
}
```

## Related
- get_groups
- get_group_types
- get_group_info
- get_groups_in_view
- get_group_members
- get_group_constraints_report

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
