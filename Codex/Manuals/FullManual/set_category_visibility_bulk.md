# set_category_visibility_bulk

- Category: ViewOps
- Purpose: Bulk-control category visibility in a view (e.g., “keep only Walls/Floors/Columns visible”).

## Overview
Provides a higher-level wrapper around `set_category_visibility` that operates on many categories at once.  
Typical uses:

- Keep only a small set of categories visible (e.g., Walls/Floors/Columns) and hide all other model categories.
- Hide only a given set of categories, leaving all others as they are.

The command is view-specific and respects view templates (with an option to detach).

## Usage
- Method: `set_category_visibility_bulk`

### Parameters
```jsonc
{
  "viewId": 11121378,                // optional, default: active graphical view
  "mode": "keep_only",               // "keep_only" | "hide_only" (default "keep_only")
  "categoryType": "Model",           // "Model" | "Annotation" | "All" (default "Model")
  "keepCategoryIds": [-2000011],     // required when mode = "keep_only"
  "hideCategoryIds": [-2000080],     // required when mode = "hide_only"
  "detachViewTemplate": true         // optional, default false
}
```

- `viewId` (int, optional)  
  Target view id. If omitted or 0, the active graphical view is used (if available).
- `mode` (string, optional, default `"keep_only"`)  
  - `"keep_only"`: Show only the categories listed in `keepCategoryIds`; hide all other categories that match `categoryType`.
  - `"hide_only"`: Hide only the categories listed in `hideCategoryIds`; leave all other categories unchanged.
- `categoryType` (string, optional, default `"Model"`)  
  Determines which categories are considered when applying bulk changes:
  - `"Model"`: Only model categories.
  - `"Annotation"`: Only annotation categories.
  - `"All"`: All category types.
- `keepCategoryIds` (int[], required when `mode == "keep_only"`)  
  List of category ids to keep visible. Values are `BuiltInCategory` integer ids (e.g. `-2000011` for `OST_Walls`).
- `hideCategoryIds` (int[], required when `mode == "hide_only"`)  
  List of category ids to hide. Values are `BuiltInCategory` integer ids.
- `detachViewTemplate` (bool, optional, default `false`)  
  When `true` and the target view has a view template applied, the template is detached before applying overrides.  
  When `false` and the view has a template, the command returns a `templateApplied` hint and does not change visibility.

### Example: keep only Walls, Floors, Structural Columns
```jsonc
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_category_visibility_bulk",
  "params": {
    "viewId": 11121378,
    "mode": "keep_only",
    "categoryType": "Model",
    "keepCategoryIds": [
      -2000011,  // OST_Walls
      -2000032,  // OST_Floors
      -2001330   // OST_StructuralColumns
    ],
    "detachViewTemplate": true
  }
}
```

### Example: hide only Furniture and Planting
```jsonc
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "set_category_visibility_bulk",
  "params": {
    "viewId": 11121378,
    "mode": "hide_only",
    "categoryType": "Model",
    "hideCategoryIds": [
      -2000080,  // OST_Furniture
      -2000040   // OST_Planting
    ],
    "detachViewTemplate": true
  }
}
```

## Result
```jsonc
{
  "ok": true,
  "viewId": 11121378,
  "mode": "keep_only",
  "categoryType": "Model",
  "changed": 54,
  "skipped": 12,
  "errors": []
}
```

- `ok` (bool): `true` on success, `false` on error.
- `viewId` (int): Id of the view where visibility was applied.
- `mode` / `categoryType`: Echo of the effective parameters.
- `changed` (int): Number of categories whose visibility state was actually changed.
- `skipped` (int): Number of categories skipped (e.g., cannot be hidden in this view type).
- `errors` (array): Any per-category errors, each with `categoryId`, `name`, `error`.

If the view has a template and `detachViewTemplate` is `false`, the command returns something like:
```jsonc
{
  "ok": true,
  "viewId": 11121378,
  "changed": 0,
  "skipped": 0,
  "templateApplied": true,
  "templateViewId": 123456,
  "appliedTo": "skipped",
  "msg": "View has a template; set detachViewTemplate:true to proceed."
}
```

## Notes

- This command operates at the **category** level (not per-element).  
  For per-element overrides, use `set_visual_override` or `batch_set_visual_override`.
- Category ids are the integer values of `BuiltInCategory` (e.g., `-2000011` for `OST_Walls`).
- `categoryType = "Model"` is usually the safest choice when building working views for geometry analysis or exports.

