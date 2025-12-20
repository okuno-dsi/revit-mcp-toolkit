# get_group_info

- Category: GroupOps
- Purpose: Get Group Info in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_group_info

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | int | no/depends |  |
| includeMembers | bool | no/depends | false |
| includeOwnerView | bool | no/depends | true |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_group_info",
  "params": {
    "elementId": 0,
    "includeMembers": false,
    "includeOwnerView": false
  }
}
```

## Related
- get_groups
- get_group_types
- get_element_group_membership
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
    },
    "includeMembers": {
      "type": "boolean"
    },
    "includeOwnerView": {
      "type": "boolean"
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
