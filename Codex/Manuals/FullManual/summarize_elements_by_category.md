# summarize_elements_by_category

- Category: AnalysisOps
- Purpose: Summarize Elements By Category in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: summarize_elements_by_category
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "summarize_elements_by_category",
  "params": {}
}
```

## Related
- summarize_family_types_by_category
- check_clashes
- diff_elements

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
