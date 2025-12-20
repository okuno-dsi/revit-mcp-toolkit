# View/Tag Troubleshooting and Best Practices

This memo summarizes pragmatic tips to reliably find and place Room Tags via MCP.

What often goes wrong

- Wrong/empty view
  - `get_elements_in_view` can return 0 if the active view is a template, sheet, or simply empty/hidden. Room Tags are view‑local, so `get_tags_in_view` in the wrong view returns 0.
- Mixed response shapes
  - `get_elements_in_view` may return a summary under `rows[]` (level/category/elementId) or detailed `elements[]`. Prefer checking `rows` first.
- Immediate queries after a view switch
  - Right after switching a view, Revit may still be regenerating. A quick `view_fit` and a short pause improves stability.
- Scoping
  - `get_tags_in_view` only collects `IndependentTag` elements in the specified view. Tags in other views never appear.

Recommended sequence (stable)

- Confirm the view: `get_current_view` → `view_fit` (optional) → small delay (0.5–1.0s).
- List elements: `get_elements_in_view { viewId, _shape: { idsOnly: true } }` to get fast counts, or `{ rows }` for a light summary.
- Detect tags: `get_tags_in_view { viewId }` and correlate by `hostElementId` against room ids.
- Place tags: resolve a room tag `typeId` (from an existing tag or by family/category), get room label point `get_room_label_point`, and call `create_tag` with `location{x,y,z}` (mm).

Revision clouds for tags (debug aid)

- Preferred: First call `get_tag_bounds_in_view { viewId, tagId }` to obtain `widthMm/heightMm`.
- Then use `create_revision_cloud_for_element_projection { viewId, revisionId, elementId: <tagId>, paddingMm, tagWidthMm, tagHeightMm }` to place a rectangular cloud around the tag.
- If projection fails with "No projectable geometry found", fallback to `create_revision_circle { viewId, revisionId, center{x,y}, radiusMm }` using the tag’s `location{x,y}` (mm) as center.
- Both commands take lengths in millimeters (converted to internal feet by the server).

Field hints

- `rows[]`: `{ levelName, levelId, categoryName, categoryId, elementId }` — quick proof of what’s visible. Look for categoryName containing "部屋タグ"/"Tag".
- `get_tags_in_view`: each item exposes `{ tagId, tagTypeId, categoryId/Name, hostElementId, location{x,y,z} }`.

When 0 results persist

- Switch to the plan view that visibly contains the targets, then retry.
- Reset view graphics if necessary (category overrides/filters can hide tags).
- Page large views: start with `idsOnly` and fetch details in small chunks.
