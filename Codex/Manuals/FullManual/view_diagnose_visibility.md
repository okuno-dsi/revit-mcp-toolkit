# view.diagnose_visibility

- Category: Diagnostics
- Purpose: Diagnose common view visibility/graphics “UI traps” (templates, crop, category visibility, temporary modes).

## Overview
- Canonical: `view.diagnose_visibility`
- Legacy alias: `diagnose_visibility`

Typical use-cases:
- “I ran a visual override command but nothing changed on screen.”
- “Elements are missing in the view.”

## Parameters
| Name | Type | Required | Default |
|---|---|---:|---:|
| viewId | integer | no | active view |
| view | object | no | active view |
| includeCategoryStates | boolean | no | true |
| includeAllCategories | boolean | no | false |
| categoryIds | integer[] | no | key categories |
| includeTemplateParamIds | boolean | no | false |
| maxTemplateParamIds | integer | no | 50 |

Notes:
- If `includeAllCategories=true`, the command enumerates all categories and returns visibility states.
- If `categoryIds` is provided, it is used as the target category set (unless `includeAllCategories=true`).

## Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view.diagnose_visibility",
  "params": { "includeAllCategories": false }
}
```

## Output (high level)
- `data.view`: id/name/type, displayStyle/detailLevel/discipline/scale, etc.
- `data.template`: template applied?, template id/name, template parameter counts (optional samples)
- `data.globalVisibility`: AreModelCategoriesHidden / AreAnnotationCategoriesHidden / ...
- `data.temporaryModes`: temporary hide/isolate, reveal hidden, temp view properties mode, etc.
- `data.crop`: crop active/visible + crop size (if available)
- `data.categories`: category visibility states (key categories by default)

## Common Interpretation
- If `data.template.applied=true`: the view template may lock Visibility/Graphics and prevent overrides from taking effect. Consider clearing the template (`clear_view_template`) or using a non-templated view.
- If `data.overrides.graphicsOverridesAllowed=false`: view type may not support graphic overrides.
- If `data.globalVisibility.areModelCategoriesHidden=true`: nothing “model” will show even if categories are enabled.

