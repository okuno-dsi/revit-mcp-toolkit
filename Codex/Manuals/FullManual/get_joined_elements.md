# get_joined_elements

- Category: ElementOps
- Purpose: Inspect join / constraint context for a single element.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It inspects one element and reports:

- Geometry joins (other elements joined via `JoinGeometryUtils`)
- Host / family relationships (host, super- / sub-components)
- Pin and group state
- Dependent elements (dimensions, tags, etc.)
- Suggested safe follow-up commands (e.g. `unjoin_elements`, `unpin_element`) and notes for actions that should be done in the Revit UI.

Use this before moving or modifying elements that may be constrained by joins, pins, groups, or dimensions.

## Usage
- Method: get_joined_elements

### Parameters
| Name      | Type   | Required      | Default |
|-----------|--------|---------------|---------|
| elementId | int    | no / one of   | 0       |
| uniqueId  | string | no / one of   |         |

At least one of `elementId` or `uniqueId` must be provided.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_joined_elements",
  "params": {
    "elementId": 39399
  }
}
```

### Example Result (shape)
```json
{
  "ok": true,
  "elementId": 39399,
  "joinedIds": [11111, 22222],
  "hostId": null,
  "superComponentId": null,
  "subComponentIds": [],
  "isPinned": false,
  "isInGroup": false,
  "groupId": null,
  "dependentIds": [39399, 39400],
  "suggestedCommands": [
    { "kind": "geometryJoin", "command": "unjoin_elements", "description": "Unjoin this element from joined partners." },
    { "kind": "pin", "command": "unpin_element", "description": "Unpin this element so it can move." }
  ],
  "notes": [
    "Host / group / dimension-related operations should generally be performed in the Revit UI."
  ]
}
```

## Related
- unjoin_elements
- unpin_element
- unpin_elements
