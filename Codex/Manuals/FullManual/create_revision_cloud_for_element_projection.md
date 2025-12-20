# create_revision_cloud_for_element_projection

- Category: RevisionCloud
- Purpose: Create Revision Cloud For Element Projection in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_revision_cloud_for_element_projection

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| debug | bool | no/depends | false |
| elementId | unknown | no/depends |  |
| elementIds | unknown | no/depends |  |
| focusMarginMm | number | no/depends | 150.0 |
| minRectSizeMm | number | no/depends | 50.0 |
| mode | string | no/depends | obb |
| paddingMm | number | no/depends | 100.0 |
| planeSource | string | no/depends | view |
| preZoom | string | no/depends |  |
| restoreZoom | bool | no/depends | false |
| revisionId | unknown | no/depends |  |
| tagHeightMm | number | no/depends | 0.0 |
| tagWidthMm | number | no/depends | 0.0 |
| uniqueId | unknown | no/depends |  |
| viewId | unknown | no/depends |  |
| widthMm | number | no/depends | 0.0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_revision_cloud_for_element_projection",
  "params": {
    "debug": false,
    "elementId": "...",
    "elementIds": "...",
    "focusMarginMm": 0.0,
    "minRectSizeMm": 0.0,
    "mode": "...",
    "paddingMm": 0.0,
    "planeSource": "...",
    "preZoom": "...",
    "restoreZoom": false,
    "revisionId": "...",
    "tagHeightMm": 0.0,
    "tagWidthMm": 0.0,
    "uniqueId": "...",
    "viewId": "...",
    "widthMm": 0.0
  }
}
```

## Related
- create_revision_cloud
- create_default_revision
- create_revision_circle
- move_revision_cloud
- delete_revision_cloud
- update_revision
- get_revision_cloud_types
- get_revision_cloud_type_parameters

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "focusMarginMm": {
      "type": "number"
    },
    "debug": {
      "type": "boolean"
    },
    "elementId": {
      "type": [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "array",
        "null"
      ]
    },
    "preZoom": {
      "type": "string"
    },
    "planeSource": {
      "type": "string"
    },
    "tagWidthMm": {
      "type": "number"
    },
    "widthMm": {
      "type": "number"
    },
    "restoreZoom": {
      "type": "boolean"
    },
    "minRectSizeMm": {
      "type": "number"
    },
    "tagHeightMm": {
      "type": "number"
    },
    "viewId": {
      "type": [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "array",
        "null"
      ]
    },
    "mode": {
      "type": "string"
    },
    "paddingMm": {
      "type": "number"
    },
    "revisionId": {
      "type": [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "array",
        "null"
      ]
    },
    "elementIds": {
      "type": [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "array",
        "null"
      ]
    },
    "uniqueId": {
      "type": [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "array",
        "null"
      ]
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
