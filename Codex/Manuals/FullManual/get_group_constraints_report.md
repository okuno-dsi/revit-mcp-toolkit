# get_group_constraints_report

- Category: GroupOps
- Purpose: Get Group Constraints Report in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_group_constraints_report

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| groupId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_group_constraints_report",
  "params": {
    "groupId": 0
  }
}
```

## Related
- get_groups
- get_group_types
- get_group_info
- get_element_group_membership
- get_groups_in_view
- get_group_members

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "groupId": {
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
