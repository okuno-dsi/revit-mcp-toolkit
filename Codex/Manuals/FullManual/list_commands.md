# list_commands

- Category: MetaOps
- Purpose: List all command method names currently registered in the running Revit add-in.

## Overview
Returns the set of **canonical** (namespaced) command method names known to the add-in’s runtime registry.

Canonical/alias policy (Step 4):
- Canonical names are **namespaced** (contain a dot), e.g. `doc.get_project_info`, `sheet.place_view`.
- Legacy names remain callable as **aliases**, but are treated as deprecated for discovery.

## Usage
- Method: `list_commands`
  - Alias (canonical): `help.list_commands`

### Parameters
| Name | Type | Required | Default | Notes |
|---|---|---:|---:|---|
| includeDeprecated | boolean | no | false | If true, also includes deprecated alias names in the output list. |
| includeDetails | boolean | no | false | If true, also returns `items[]` with `method/deprecated/canonical/aliases`. |

Notes:
- Extra/unknown params are ignored (backward compatible).

### Example Result (shape)
```jsonc
{
  "ok": true,
  "commands": ["doc.get_project_info", "doc.get_open_documents", "sheet.place_view", "..."],
  "canonicalOnly": true,
  "deprecatedIncluded": false
}
```
