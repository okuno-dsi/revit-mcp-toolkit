# get_selected_element_ids

- Category: Misc
- Purpose: Get Selected Element Ids in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_selected_element_ids
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_selected_element_ids",
  "params": {}
}
```

## Related
- stash_selection
- restore_selection
- get_element_info

### Params Schema
```json
{
  "type": "object",
  "properties": {}
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
