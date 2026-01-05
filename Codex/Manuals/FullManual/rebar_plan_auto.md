# rebar_plan_auto

- Category: Rebar
- Purpose: Build an “auto rebar plan” for selected structural hosts (columns/beams) without modifying the model.

## Overview
v1 plan rules:
- Structural Columns: 4 corner main bars + 1 ties (stirrups/ties) set.
- Structural Framing (beams): main bars (top/bottom) + 1 stirrups set.
  - Default main bars: top=2, bottom=2 (override via `options.beamMainTopCount/beamMainBottomCount` or mapping beam keys).

Geometry is approximate; beams prefer physical extents derived from solid geometry (best-effort) so rebars align to actual concrete faces. Falls back to bbox when geometry is unavailable. LocationCurve axis range is captured as debug info when available. Use as automation scaffolding, not as structural design validation.

Beam note (mapping-driven attributes):
- If the matched `RebarMapping.json` profile defines beam attribute keys (e.g. `Beam.Attr.MainBar.DiameterMm`, `Beam.Attr.MainBar.TopCount`, `Beam.Attr.Stirrup.DiameterMm`, `Beam.Attr.Stirrup.PitchMidMm`),
  and `options.beamUseTypeParams` is true, this command can prefer them (best-effort) to decide:
  - top/bottom main bar counts
  - stirrup spacing (mid/start/end pitch → first non-zero)
  - bar type selection by diameter (does not assume bar type naming)
- Parameter names vary by family/author/language; register them in `RebarMapping.json` profiles (e.g. `rc_beam_attr_jp_v1`) instead of hardcoding.

Beam geometry options (v1 scaffolding):
- Main bar axis can be extended/shortened beyond the bbox via `beamMainBarStartExtensionMm` / `beamMainBarEndExtensionMm`.
- By default, beam main bars try to embed into support columns by `0.75 * columnWidthAlongBeamAxis` (best-effort). Disable/override via options below.
- Optional 90-degree bends at the ends can be represented as extra line segments in the planned curves
  (`beamMainBarStartBendLengthMm` / `beamMainBarEndBendLengthMm`, direction `up|down`).
- Stirrups loop start corner can be rotated via `beamStirrupStartCorner` (top-left is common).
- Beam stirrups can optionally use hook types (e.g. 135°) by generating an open polyline and applying `RebarHookType` at both ends (best-effort).
- Note: some `RebarHookType` are only compatible with certain `RebarStyle`. If you request a hook name containing `標準フック`, the tool switches the stirrup/tie action to `style:"Standard"` (best-effort).
- Column main bars can also be extended and have optional 90-degree bends.
- Column ties can be offset at start/end and can optionally use hook types (e.g. 135°) (best-effort).
- Beam stirrups start/end try to align to column faces (support faces) by inspecting Structural Columns’ bounding boxes (best-effort):
  - first tries joined columns
  - if not joined, searches columns near each beam endpoint

## Usage
- Method: `rebar_plan_auto`

### Parameters
| Name | Type | Required | Default | Notes |
|---|---|---:|---:|---|
| hostElementIds | int[] | no |  | If omitted, uses selection (`useSelectionIfEmpty=true`). |
| useSelectionIfEmpty | bool | no | true | Use current selection if `hostElementIds` is empty. |
| profile | string | no |  | Mapping profile name for `RebarMapping.json` (optional). |
| options | object | no |  | Planning options (overrides). |
| options.tagComments | string | no | `RevitMcp:AutoRebar` | Written into created rebars’ `Comments` when applying the plan. |
| options.mainBarTypeName | string | no |  | Overrides mapping `Common.MainBar.BarType`. |
| options.tieBarTypeName | string | no |  | Overrides mapping `Common.TieBar.BarType` (used for ties/stirrups). |
| options.includeMainBars | bool | no | true | If false, only ties/stirrups are planned. |
| options.includeTies | bool | no | true | Column ties on/off. |
| options.includeStirrups | bool | no | true | Beam stirrups on/off. |
| options.beamUseTypeParams | bool | no | true | If true, uses beam attribute keys from `RebarMapping.json` (profile-dependent) to override counts/pitch and choose bar types by diameter. |
| options.beamMainTopCount | int | no |  | Beam main bar count on top layer (user override). |
| options.beamMainBottomCount | int | no |  | Beam main bar count on bottom layer (user override). |
| options.beamMainBarStartExtensionMm | number | no | 0 | Beam main bar start extension (mm). Positive extends; negative shortens. |
| options.beamMainBarEndExtensionMm | number | no | 0 | Beam main bar end extension (mm). Positive extends; negative shortens. |
| options.beamMainBarEmbedIntoSupportColumns | bool | no | true | If true, extends main bars into support columns (best-effort). |
| options.beamMainBarEmbedIntoSupportColumnRatio | number | no | 0.75 | Embed length = ratio * support column width (along beam axis). |
| options.beamSupportSearchRangeMm | number | no | 1500 | Support column search range around each beam end point. |
| options.beamSupportFaceToleranceMm | number | no | 250 | Face distance tolerance when matching a support column to a beam end face. |
| options.beamStirrupUseHooks | bool | no | false | If true, beam stirrups are created as an open polyline and hooks are applied at both ends (best-effort). |
| options.beamStirrupHookAngleDeg | number | no | 0 | Hook angle in degrees (e.g. `135`). Used to find a matching `RebarHookType` when `beamStirrupHookTypeName` is not provided. |
| options.beamStirrupHookTypeName | string | no |  | Optional exact `RebarHookType` name (preferred over angle). |
| options.beamStirrupHookOrientationStart | string | no | `left` | `left`/`right` (Revit hook orientation). |
| options.beamStirrupHookOrientationEnd | string | no | `right` | `left`/`right` (Revit hook orientation). |
| options.beamStirrupStartOffsetMm | number | no | 0 | Start offset from beam start face (mm). |
| options.beamStirrupEndOffsetMm | number | no | 0 | End offset from beam end face (mm). |
| options.columnMainBarBottomExtensionMm | number | no | 0 | Column main bar bottom extension (mm). Positive extends; negative shortens. |
| options.columnMainBarTopExtensionMm | number | no | 0 | Column main bar top extension (mm). Positive extends; negative shortens. |
| options.columnMainBarStartBendLengthMm | number | no | 0 | Column main bar start 90-degree bend length (mm). |
| options.columnMainBarEndBendLengthMm | number | no | 0 | Column main bar end 90-degree bend length (mm). |
| options.columnMainBarStartBendDir | string | no | `none` | `up`/`down`/`none` (relative to world Z). |
| options.columnMainBarEndBendDir | string | no | `none` | `up`/`down`/`none` (relative to world Z). |
| options.columnTieBottomOffsetMm | number | no | 0 | Bottom offset from column bottom face for first tie (mm). |
| options.columnTieTopOffsetMm | number | no | 0 | Top offset from column top face for last tie (mm). |
| options.columnTieUseHooks | bool | no | false | If true, column ties are created as an open polyline and hooks are applied at both ends (best-effort). |
| options.columnTieHookAngleDeg | number | no | 0 | Hook angle in degrees (e.g. `135`). Used to find a matching `RebarHookType` when `columnTieHookTypeName` is not provided. |
| options.columnTieHookTypeName | string | no |  | Optional exact `RebarHookType` name (preferred over angle). |
| options.columnTieHookOrientationStart | string | no | `left` | `left`/`right`. |
| options.columnTieHookOrientationEnd | string | no | `right` | `left`/`right`. |
| options.beamMainBarStartBendLengthMm | number | no | 0 | Beam main bar start 90deg bend length (mm). 0 disables. |
| options.beamMainBarEndBendLengthMm | number | no | 0 | Beam main bar end 90deg bend length (mm). 0 disables. |
| options.beamMainBarStartBendDir | string | no | `none` | `up` / `down` / `none` (relative to local up axis). |
| options.beamMainBarEndBendDir | string | no | `none` | `up` / `down` / `none` (relative to local up axis). |
| options.beamStirrupStartCorner | string | no | `top_left` | `bottom_left` / `bottom_right` / `top_right` / `top_left` (rotates loop order). |
| options.layout | object | no |  | Overrides layout spec for created ties/stirrups (same schema as `rebar_layout_update.layout`). |
| options.includeMappingDebug | bool | no | false | If true, includes mapping sources details in the plan. |
| options.preferMappingArrayLength | bool | no | false | If true, uses mapping `Common.Arrangement.ArrayLength` when present; otherwise uses computed length. |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rebar_plan_auto",
  "params": {
    "useSelectionIfEmpty": true,
    "options": {
      "mainBarTypeName": "D22",
      "tieBarTypeName": "D10"
    }
  }
}
```

## Response Notes
- Returns a `plan`-like object with:
  - `planVersion`
  - `mappingStatus` (load status of `RebarMapping.json`)
  - `hosts[]` each containing `actions[]` with planned curves and layout.

## Related
- `rebar_apply_plan`
- `rebar_mapping_resolve`
- `rebar_layout_update_by_host`
