# get_groups

- Category: GroupOps
- Purpose: Get Groups in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_groups

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends | 100 |
| skip | int | no/depends | 0 |
| viewScoped | bool | no/depends | false |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_groups",
  "params": {
    "count": 0,
    "skip": 0,
    "viewScoped": false
  }
}
```

## Related
- get_group_types
- get_group_info
- get_element_group_membership
- get_groups_in_view
- get_group_members
- get_group_constraints_report

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "skip": {
      "type": "integer"
    },
    "viewScoped": {
      "type": "boolean"
    },
    "count": {
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
