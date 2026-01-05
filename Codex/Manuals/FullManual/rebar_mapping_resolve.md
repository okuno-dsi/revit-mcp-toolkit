# rebar_mapping_resolve

- Category: Rebar
- Purpose: Resolve logical keys from `RebarMapping.json` for selected host elements (debug/inspection).

## Overview
This command helps you validate the mapping configuration without running an auto-rebar pipeline.

The add-in loads `RebarMapping.json` (best-effort) from:
- `REVITMCP_REBAR_MAPPING_PATH` (explicit override), or
- `%LOCALAPPDATA%\\RevitMCP\\RebarMapping.json`, or
- `%USERPROFILE%\\Documents\\Codex\\Design\\RebarMapping.json`, or
- the add-in folder (next to the DLL).

### Profile Selection (when `profile` is omitted)
The add-in auto-selects a profile using (best-effort):
- `appliesTo.categories`
- optional `appliesTo.familyNameContains` / `appliesTo.typeNameContains`
- optional `appliesTo.requiresTypeParamsAny` / `appliesTo.requiresInstanceParamsAny`
- then `priority` (higher wins), then more specific profiles win

### Supported Source Kinds
`map.{key}.sources[].kind` supports:
- `constant`, `derived`, `instanceParam`, `typeParam`, `builtInParam`
- `instanceParamGuid`, `typeParamGuid` (shared parameter GUID; language-independent)

### Numeric Length Notes
For `double`/`int` values with `unit:"mm"`:
- if the underlying parameter spec is `Length`, the add-in converts internal units (ft → mm)
- otherwise it treats the stored number as-is (useful for RC-family parameters that store “mm” as plain numbers)

## Usage
- Method: `rebar_mapping_resolve`

### Parameters
| Name | Type | Required | Default | Notes |
|---|---|---:|---:|---|
| hostElementIds | int[] | no |  | Host element ids (structural columns/framing, etc.). |
| useSelectionIfEmpty | bool | no | true | If true and `hostElementIds` is empty, use current selection. |
| profile | string | no |  | Profile name. If omitted, auto-selects by category and falls back to `default`. |
| keys | string[] | no |  | If omitted, resolves all keys in the profile. |
| includeDebug | bool | no | false | If true, includes per-key source selection details. |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rebar_mapping_resolve",
  "params": {
    "useSelectionIfEmpty": true,
    "profile": "default",
    "keys": ["Host.Section.Width", "Host.Cover.Other"],
    "includeDebug": true
  }
}
```

## Related
- `rebar_layout_inspect`
