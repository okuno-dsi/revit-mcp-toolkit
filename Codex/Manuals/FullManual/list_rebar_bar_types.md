# list_rebar_bar_types

- Category: Rebar
- Purpose: List `RebarBarType` definitions (bar size types) available in the current document.

## Usage
- Method: `list_rebar_bar_types`

### Parameters
| Name | Type | Required | Default | Notes |
|---|---|---:|---:|---|
| includeCountByDiameter | bool | no | true | Adds `countByDiameterMm` summary. |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "list_rebar_bar_types",
  "params": { "includeCountByDiameter": true }
}
```

## Notes
- If this returns `count: 0`, the project currently has no bar types; auto-rebar commands cannot create rebars until bar types are available.
- Use `import_rebar_types_from_document` to import bar types (and shapes/hooks) from a donor `.rvt/.rte` (recommended: a structural template).

