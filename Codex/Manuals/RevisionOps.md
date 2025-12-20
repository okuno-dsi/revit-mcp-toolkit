# Revision Operations (Revit)

This note summarizes the revision-related commands in RevitMCPAddin and the latest robustness updates.

## Commands

- `list_revisions`
  - Lists revisions. Optional `includeClouds:boolean` and `cloudFields:string[]` to query related revision clouds sparsely.
  - Returns `totalCount`, `numberingMode`, and an array of items with basic fields.
  - Example:
    - `{ method:"list_revisions", params:{ includeClouds:false } }`

- `list_sheet_revisions`
  - Lists revisions attached to sheets. Optional `sheetIds:int[]` and `includeRevisionDetails:boolean`.
  - Example:
    - `{ method:"list_sheet_revisions", params:{ sheetIds:[123,456], includeRevisionDetails:true } }`

- `create_default_revision`
  - Appends a new revision to the document and returns its `revisionId`.

- `update_revision` (stabilized)
  - Updates existing revision fields safely. Supports both `revisionId` and `uniqueId`.
  - Supported fields:
    - `description:string`, `issuedBy:string`, `issuedTo:string`, `revisionDate:string`, `issued:boolean`
    - Numbering sequence via either `revisionNumberingSequenceId:int` or `revisionNumberingSequenceName:string`
  - Notes:
    - `RevisionNumber` itself is read-only; use numbering sequence assignment instead when supported by the API.
    - Sequence assignment is attempted defensively; if the API doesn’t allow it in the current environment, the command ignores it gracefully.
  - Examples:
    - `{ method:"update_revision", params:{ revisionId: 123, issued:true, revisionDate:"2025-10-23" } }`
    - `{ method:"update_revision", params:{ uniqueId:"...", description:"Grid update", revisionNumberingSequenceName:"Project Default" } }`

- `create_revision_cloud`
  - Creates revision clouds in a view for a given revision.
  - Params:
    - `viewId:int`, `revisionId:int`, `curveLoops: [ [ {start:{x,y,z}, end:{x,y,z}}, ... ], ... ]` (mm units for points)

- `create_revision_circle`
  - Creates a circular revision cloud by center and radius (mm).
  - Params: `{ viewId:int, revisionId:int, center:{x:number,y:number,z?:number}, radiusMm:number, segments?:number }`
  - Notes: `segments` defaults to 24; the circle is approximated by small arc segments. Useful as a fallback when element-projection is not applicable.

- `get_revision_cloud_parameters` / `set_revision_cloud_parameter`
  - Gets/sets parameters of a revision cloud. Double values are converted between mm and internal units.
  - Parameter keys accepted (priority): `builtInId` → `builtInName` → `guid` → `paramName`.
    - Example (builtInName): `{ method:"set_revision_cloud_parameter", params:{ elementId: 123, builtInName:"ALL_MODEL_MARK", value:"A-101" } }`
    - Example (paramName): `{ method:"set_revision_cloud_parameter", params:{ elementId: 123, paramName:"Mark", value:"A-101" } }`

Implementation notes (stability)

- Input units are interpreted as millimeters and converted to Revit internal feet.
- For `create_revision_cloud`, curve loops are validated (closure, orientation) and snapped to the view plane to avoid geometry exceptions.
- For `create_revision_cloud_for_element_projection`, tags (IndependentTag) are supported via view-projected bounding boxes; if unavailable, tag head position is used to synthesize a small rectangle.

## Recent Stabilization Changes

- `update_revision`
  - Accepts `revisionId` or `uniqueId` for resolution, with clear error messages.
  - Adds support for `issued:boolean` and setting numbering sequence by name or id.
  - Wraps all updates in a guarded transaction with commit/rollback and detailed exceptions.
  - Avoids direct writes to read-only fields and quietly skips unsupported sequence setter calls.

## Tips

- When heavy datasets are expected, prefer summary-style queries first (e.g., `list_revisions` with `includeClouds:false`) and request details incrementally.
- For cross-document tooling, always pass `revisionId` integers; use `uniqueId` when id-space stability across sessions is necessary.
