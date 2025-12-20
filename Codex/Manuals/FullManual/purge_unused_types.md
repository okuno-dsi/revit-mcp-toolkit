# purge_unused_types

- Category: TypeOps
- Purpose: Purge Unused Types in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: purge_unused_types

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| category | string | no/depends |  |
| deleteInstances | bool | no/depends | false |
| dryRun | bool | no/depends | false |
| familyName | string | no/depends |  |
| force | bool | no/depends | false |
| purgeAllUnusedInCategory | bool | no/depends | false |
| reassignToFamilyName | string | no/depends |  |
| reassignToTypeId | int | no/depends |  |
| reassignToTypeName | string | no/depends |  |
| reassignToUniqueId | string | no/depends |  |
| typeId | int | no/depends |  |
| typeName | string | no/depends |  |
| uniqueId | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "purge_unused_types",
  "params": {
    "category": "...",
    "deleteInstances": false,
    "dryRun": false,
    "familyName": "...",
    "force": false,
    "purgeAllUnusedInCategory": false,
    "reassignToFamilyName": "...",
    "reassignToTypeId": 0,
    "reassignToTypeName": "...",
    "reassignToUniqueId": "...",
    "typeId": 0,
    "typeName": "...",
    "uniqueId": "..."
  }
}
```

## Related
- delete_type_if_unused
- force_delete_type
- rename_types_bulk
- rename_types_by_parameter

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "typeName": {
      "type": "string"
    },
    "reassignToUniqueId": {
      "type": "string"
    },
    "familyName": {
      "type": "string"
    },
    "category": {
      "type": "string"
    },
    "purgeAllUnusedInCategory": {
      "type": "boolean"
    },
    "reassignToTypeName": {
      "type": "string"
    },
    "deleteInstances": {
      "type": "boolean"
    },
    "dryRun": {
      "type": "boolean"
    },
    "uniqueId": {
      "type": "string"
    },
    "typeId": {
      "type": "integer"
    },
    "reassignToTypeId": {
      "type": "integer"
    },
    "reassignToFamilyName": {
      "type": "string"
    },
    "force": {
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
