# set_view_template

- Category: ViewOps
- Purpose: View Delete / View Template Assign-Clear / Save as Template / Rename Template

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: set_view_template

### Parameters
| Name | Type | Required | Default | Description |
|---|---|---|---|---|
| viewId | int | (one of view spec required) | none | Target view ElementId. |
| uniqueId | string | same as above | none | Target view UniqueId, as an alternative to `viewId`. |
| templateViewId | int | (one of template spec required\*) | none | ElementId of the view template to apply. Must be a template view. |
| templateName | string | same as above | none | Name of the view template (case-insensitive). Used instead of `templateViewId`. |
| clear | bool | no/depends | false | When `true`, clears the view template and ignores `templateViewId`/`templateName`. |

\* When `clear:false` (the default), either `templateViewId` or `templateName` must be provided to apply a template.

### Example Request (apply template by id)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_view_template",
  "params": {
    "viewId": 123456,
    "templateViewId": 234567
  }
}
```

### Example Request (apply template by name)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_view_template",
  "params": {
    "viewId": 123456,
    "templateName": "Arch Plan - Standard"
  }
}
```

### Example Request (clear template)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_view_template",
  "params": {
    "viewId": 123456,
    "clear": true
  }
}
```

## Related
- get_current_view
- get_view_info
- save_view_state
- restore_view_state
- create_view_plan
- create_section
- create_elevation_view
- compare_view_states

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "viewId": {
      "type": "integer"
    },
    "uniqueId": {
      "type": "string"
    },
    "templateViewId": {
      "type": "integer"
    },
    "templateName": {
      "type": "string"
    },
    "clear": {
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
