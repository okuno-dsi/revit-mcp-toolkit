# Updated View/Visualization Commands (Resilient, Non‑blocking)

These commands were upgraded to avoid UI stalls and improve reliability in templated or heavy views. Key improvements:
- View resolution: uses `ActiveGraphicalView` when `viewId` is omitted
- Template handling: `detachViewTemplate:true` or `autoWorkingView:true` (duplicate/3D fallback) to avoid template locks
- Time slicing: `batchSize`, `maxMillisPerTx`, `startIndex`, `refreshView` to break long work into short transactions and resume via `nextIndex`

All examples use: `python Scripts/Reference/send_revit_command_durable.py --port 5210 --command <METHOD> --params '<JSON>'`.

## set_visual_override
- Purpose: Per‑element fill/line color and transparency overrides in a view.
- New params:
  - `autoWorkingView` (bool, default true): when a View Template blocks overrides, auto‑create a working 3D view and apply there
  - `detachViewTemplate` (bool): detach the view template before applying
  - `batchSize` (default 800), `maxMillisPerTx` (default 3000), `startIndex`, `refreshView`
- View resolution: `viewId` optional; falls back to active graphical view.
- Color: pass `r,g,b` at top level; transparency 0..100 (higher = more transparent)
- Example (single element):
  - `{"elementId":277831,"r":0,"g":200,"b":255,"transparency":20,"__smoke_ok":true}`
- Result keys: `{ ok, viewId, count, templateApplied, completed, nextIndex?, batchSize, elapsedMs }`

## clear_visual_override
- Purpose: Clear per‑element overrides (leave category overrides untouched).
- New params: `autoWorkingView`, `detachViewTemplate`, `batchSize`(800), `maxMillisPerTx`(3000), `startIndex`, `refreshView`
- View resolution: `viewId` optional; uses active graphical view.
- Examples:
  - By single element: `{"elementId":277831,"__smoke_ok":true}`
  - By many elements: `{"elementIds":[277831,277832,277833],"__smoke_ok":true}`
- Result keys: `{ ok, viewId, count, completed, nextIndex?, batchSize, elapsedMs }`

## set_category_override
- Purpose: Apply overrides to categories in a view (line colors, optional solid fill and transparency).
- New params: `autoWorkingView`, `detachViewTemplate`, `batchSize`(200), `maxMillisPerTx`(3000), `startIndex`, `refreshView`
- View resolution: `viewId` optional; uses active graphical view.
- Color & fill: `{"color":{"r":255,"g":50,"b":0},"applyFill":true,"transparency":30}`
- Example: `{"categoryIds":[-2000011],"color":{"r":255,"g":50,"b":0},"applyFill":true,"transparency":30,"__smoke_ok":true}`
- Result keys: `{ ok, viewId, overridden, skipped, completed, nextIndex?, batchSize, elapsedMs }`

## apply_conditional_coloring
- Purpose: Color elements in a view by parameter value ranges.
- New params:
  - `autoWorkingView` (default true) or `detachViewTemplate` for template views
  - Time slicing: `batchSize`(800), `maxMillisPerTx`(3000), `startIndex`, `refreshView`
- View resolution: `viewId` optional; uses active graphical view.
- Parameter spec: use `param.name` or `param.parameterId`/`param.builtinId` (IDs are locale‑safe)
- Units: `units: "mm|m2|m3|deg|auto"` for Double parameters
- Rules example:
  - `{"categoryIds":[-2000011],"param":{"parameterId":-1004005},"units":"mm","rules":[{"lte":5000,"color":{"r":255,"g":0,"b":0},"transparency":20,"applyFill":true},{"lte":20000,"color":{"r":0,"g":180,"b":0},"transparency":10,"applyFill":true}],"__smoke_ok":true}`
- Result keys: `{ ok, viewId, updated, skippedCount, skipped[], completed, nextIndex?, batchSize, elapsedMs }`

## apply_quick_color_scheme (Color Fill)
- Purpose: Build/apply a Color Fill scheme (categorical or range) and optional legend.
- New behavior in templated views: `detachViewTemplate:true` or `autoWorkingView:true` (duplicate view with detailing, detach template, and apply)
- View resolution: `viewId` optional; uses active or active graphical view.
- Example:
  - `{"viewId":312,"categoryId":-2000160,"mode":"sequential","palette":"YlOrRd","bins":7,"createLegend":true,"legendOrigin":{"x":0,"y":0,"z":0}}`
- Result keys: `{ ok, viewId, schemeId, legendId?, note }`

## create_spatial_volume_overlay
- Purpose: Visualize Room/Space volumes via DirectShape extrusion and apply view overrides (fill color + transparency).
- New params: batched/time‑sliced execution
  - `batchSize`(default 200), `maxMillisPerTx`(default 3000), `startIndex`, `refreshView`
- Required: `elementIds` (Room/Space ids); `target: "room"|"space"`; `heightMm?` (fallback tries element height; defaults 2500)
- Color object: `{"color":{"r":0,"g":120,"b":215},"transparency":60}`
- View resolution: `viewId` optional; uses active graphical view.
- Example: `{"elementIds":[604,702],"target":"room","heightMm":2600,"color":{"r":0,"g":120,"b":215},"transparency":40,"__smoke_ok":true}`
- Result keys: `{ ok, viewId, created[], skipped[], errors[], completed, nextIndex?, batchSize }`

## hide_elements_in_view
- Purpose: UI‑like per‑element hide in a view.
- New behavior: `viewId` optional; uses active graphical view. Existing batching/`detachViewTemplate` remain.
- Example: `{"elementIds":[277831,277832],"__smoke_ok":true}`
- Result keys: `{ ok, viewId, hiddenCount, skipped[], errors[], completed, nextIndex?, batchSize, elapsedMs }`

## Notes
- Always avoid sending `viewId: 0` or `elementId: 0`.
- For write operations, prefer running a `smoke_test` first or use the `*_safe.ps1` helpers where available.
- In heavy models, favor smaller `batchSize` (e.g., 200) and keep `maxMillisPerTx` around 2–5 seconds to maintain UI responsiveness.



## Additional Tips
- Non-graphical views (e.g., Project Browser) cannot be duplicated. Activate a graphical view first, or create a plan view via `create_view_plan` and use that as a base for duplication.
- When templates lock visibility/overrides, prefer `detachViewTemplate:true` for the operation and reapply templates/categories/filters/worksets via `save_view_state` → `restore_view_state` (`apply.hiddenElements` as needed).
- To debug why elements are not visible, call `audit_hidden_in_view` with `_filter.includeKinds=["category","explicit","filter","temp"]`. If the result says `category_hidden`, use `set_category_visibility`; if `hidden_in_view`, use `unhide_elements_in_view` (batched) to recover, then apply your isolation rules.



