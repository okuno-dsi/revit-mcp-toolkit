# get_rooms

- Category: Room
- Purpose: Get Rooms in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_rooms

### Parameters
| Name | Type | Required | Default | Description |
|---|---|---|---|---|
| compat | bool | no | false | Also returns `roomsById` for compatibility |
| count | int | no | all | Page size |
| level | string | no |  | Exact level name filter |
| nameContains | string | no |  | Case-insensitive room name contains |
| skip | int | no | 0 | Page offset |
| docGuid | string | no |  | Target document `docGuid` / `docKey` |
| docTitle | string | no |  | Target document title |
| docPath | string | no |  | Target document full path |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_rooms",
  "params": {
    "compat": false,
    "count": 0,
    "level": "...",
    "nameContains": "...",
    "skip": 0
  }
}
```

## Related
- find_room_placeable_regions
- summarize_rooms_by_level
- validate_create_room
- get_room_params
- set_room_param
- get_room_boundary
- create_room
- delete_room

### Notes
- If `docGuid` / `docTitle` / `docPath` are omitted, the active document is used.
- The same document hints are also accepted via `meta.extensions`.
- `count=0` can be used for a lightweight count-only probe.

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "skip": {
      "type": "integer"
    },
    "count": {
      "type": "integer"
    },
    "level": {
      "type": "string"
    },
    "nameContains": {
      "type": "string"
    },
    "compat": {
      "type": "boolean"
    },
    "docGuid": {
      "type": "string"
    },
    "docTitle": {
      "type": "string"
    },
    "docPath": {
      "type": "string"
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
