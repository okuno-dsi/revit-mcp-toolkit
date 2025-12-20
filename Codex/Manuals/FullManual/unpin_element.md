# unpin_element

- Category: Misc
- Purpose: Clear the `Pinned` flag on a single element.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It resolves a single element by `elementId` or `uniqueId` and, if it is pinned, sets `Pinned = false` inside a Revit transaction.

If the element is already unpinned, the command is a no-op and returns `changed: false`.

## Usage
- Method: unpin_element

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
  "method": "unpin_element",
  "params": {
    "elementId": 39399
  }
}
```

### Example Result
```json
{
  "ok": true,
  "elementId": 39399,
  "uniqueId": "f7d54af9-82bd-43de-9210-0e8af02555e6-000099e7",
  "changed": false,
  "wasPinned": false
}
```

## Related
- get_joined_elements
- unpin_elements
