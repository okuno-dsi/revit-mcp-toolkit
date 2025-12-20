# unpin_elements

- Category: Misc
- Purpose: Batch-clear the `Pinned` flag on multiple elements.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It takes an array of `elementIds`, attempts to unpin each element inside a single Revit transaction, and reports how many were successfully changed.

Elements that are already unpinned are left as-is and do not count as `changed`.

## Usage
- Method: unpin_elements

### Parameters
| Name       | Type    | Required | Default |
|------------|---------|----------|---------|
| elementIds | int[]   | yes      | []      |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "unpin_elements",
  "params": {
    "elementIds": [39399, 77829]
  }
}
```

### Example Result
```json
{
  "ok": true,
  "requested": 2,
  "processed": 2,
  "changed": 0,
  "failedIds": []
}
```

## Related
- get_joined_elements
- unpin_element
