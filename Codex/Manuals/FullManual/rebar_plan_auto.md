# rebar_plan_auto

- Category: Rebar
- Purpose: Build an “auto rebar plan” for selected structural hosts (columns/beams) without modifying the model.

## Overview
v1 plan rules:
- Structural Columns: perimeter main bars (see `options.columnMainBarsPerFace`) + 1 ties set (or multiple sets when joint pattern is enabled).
- Structural Framing (beams): main bars (top/bottom) + 1 stirrups set.
  - Default main bars: top=2, bottom=2 (override via `options.beamMainTopCount/beamMainBottomCount` or mapping beam keys).
  - If mapping provides 2nd/3rd layer counts (e.g. `Beam.Attr.MainBar.TopCount2`), the tool stacks up to 3 layers per side (heuristic).
- Multi-layer pitch uses `RebarBarClearanceTable.json` (center-to-center) when available; otherwise falls back to `barDiameter + options.beamMainBarLayerClearMm`.

Related validation:
- Use `rebar_spacing_check` to validate actual center-to-center distances of created single bars in the model (best-effort).

Geometry is approximate; beams prefer physical extents derived from solid geometry (best-effort) so rebars align to actual concrete faces. Falls back to bbox when geometry is unavailable. LocationCurve axis range is captured as debug info when available. Use as automation scaffolding, not as structural design validation.

For columns, the per-face perimeter bar count prefers mapping key `Column.Attr.MainBar.BarsPerFace` when available (parameter names are resolved via `RebarMapping.json` profiles).

Cover handling:
- The tool reads the host instance’s Rebar Cover settings (e.g. `CLEAR_COVER_TOP/BOTTOM/OTHER` → `RebarCoverType.CoverDistance`) when available, and uses them for bar placement.
- If mapping provides `Host.Cover.Top/Bottom/Other`, it overrides the instance values (best-effort). This is useful when a project/family stores cover as custom instance parameters (e.g. `かぶり厚-上`) instead of (or in addition to) Revit’s Rebar Cover Type.
- The resolved values are returned under `hosts[].coversMm` with per-face `sources` (`hostInstance` / `mapping` / `default`).
- If the host element has explicit per-face cover instance parameters (e.g. `かぶり厚-上/下/左/右`), the tool uses them for **cross-section placement** (best-effort), and returns them as `hosts[].coverFacesMm` (up/down/left/right). This is intentionally separate from the Revit cover type IDs, and does not change them.

Cover confirmation (safety):
- Cover parameter naming differs by model/family. If the tool cannot confidently decide which cover parameters to read (or any raw cover is below `options.coverMinMm`), it returns `ok:false` with `code:"COVER_CONFIRMATION_REQUIRED"` and does not generate/apply rebar.
- Review `hosts[].coverCandidateParamNames` (cover-like parameter name candidates), `hosts[].coverFacesMmRaw`, and `hosts[].coversMm`, then either:
  - Rerun with `options.coverConfirmProceed:true` (explicit acknowledgement; still clamped if `options.coverClampToMin:true`), or
  - Provide explicit per-face parameter name hints via `options.coverParamNames` (`up/down/left/right` arrays).

Tip: Before hardcoding parameter names, use the `snoop` command to confirm the exact parameter names and types in the current project/family.

Bar type resolution:
- First tries an exact `RebarBarType` name match (from `options.*BarTypeName` or mapping `Common.*BarType`).
- If not found and the text contains a diameter like `D25`, falls back to searching an existing `RebarBarType` by diameter (to avoid depending on naming conventions). The resolved result is returned as `mainBarTypeResolve` / `tieBarTypeResolve` per host.

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
- Beam stirrups can optionally use hook types (e.g. 135°). The tool keeps the stirrup curve as a **closed loop** and applies hook types/rotations via instance parameters after creation (best-effort).
- Note: some `RebarHookType` are only compatible with certain `RebarStyle`. For ties/stirrups (`style:"StirrupTie"`), the tool prefers `StirrupTie`-compatible hook types. If an incompatible (Standard-only) hook name is provided (e.g. `標準フック - 135 度`), the tool ignores that exact name and falls back to angle-based resolution (e.g. `135°`) to pick a compatible hook type. If no compatible hook type exists in the project, hooks are disabled and the plan records a warning (best-effort).
- Column main bars can also be extended and have optional 90-degree bends.
- Column ties can be offset at start/end and can optionally use hook types (e.g. 135°) (best-effort).
- Column ties can optionally use a fully custom pattern via mapping key `Column.Attr.Tie.PatternJson` or `options.columnTiePattern` (recommended, best-effort).
  - `beam_top` / `beam_bottom` references are estimated from `Host.Section.Height` + LocationCurve Z (= derived from level/offset) (best-effort; may use bbox as a hint).
  - Legacy `options.columnTieJointPatternEnabled` remains for backward compatibility (internally translated to `columnTiePattern`).
- If a user manually adjusted hook settings on existing tagged rebars in the same host, and no hook spec is provided in options, the tool can inherit them when `options.hookAutoDetectFromExistingTaggedRebar=true` (default, best-effort). This applies to column ties and beam stirrups.
  - JP UI parameter names observed:
    - `始端のフック` / `終端のフック`
    - `始端でのフックの回転` / `終端でのフックの回転` (some environments use `始端のフックの回転` / `終端のフックの回転`)
  - Angle caveat: some angle parameters may normalize `180°` to `0°`. If you need a UI display close to `180°`, a small offset like `179.9°` can be a practical workaround.
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
| options.coverConfirmEnabled | bool | no | true | If true, may require explicit confirmation when cover parameters are missing/ambiguous or below `coverMinMm`. |
| options.coverConfirmProceed | bool | no | false | Explicit acknowledgement to proceed when confirmation is required (`COVER_CONFIRMATION_REQUIRED`). |
| options.coverMinMm | number | no | 40 | Minimum cover (mm) used by the safety policy. |
| options.coverClampToMin | bool | no | true | If true, clamps per-face cover values below `coverMinMm` up to the minimum (best-effort). |
| options.coverParamNames | object | no |  | Optional per-face cover parameter names: `{ up: string[]; down: string[]; left: string[]; right: string[] }`. |
| options.beamMainTopCount | int | no |  | Beam main bar count on top layer (user override). |
| options.beamMainBottomCount | int | no |  | Beam main bar count on bottom layer (user override). |
| options.beamMainBarLayerClearMm | number | no | 30 | Beam main bar layer clear distance (mm). Used only when `RebarBarClearanceTable.json` has no entry for the bar diameter; fallback pitch = barDiameter + clear. |
| options.beamMainBarStartExtensionMm | number | no | 0 | Beam main bar start extension (mm). Positive extends; negative shortens. |
| options.beamMainBarEndExtensionMm | number | no | 0 | Beam main bar end extension (mm). Positive extends; negative shortens. |
| options.beamMainBarEmbedIntoSupportColumns | bool | no | true | If true, extends main bars into support columns (best-effort). |
| options.beamMainBarEmbedIntoSupportColumnRatio | number | no | 0.75 | Embed length = ratio * support column width (along beam axis). |
| options.beamSupportSearchRangeMm | number | no | 1500 | Support column search range around each beam end point. |
| options.beamSupportFaceToleranceMm | number | no | 250 | Face distance tolerance when matching a support column to a beam end face. |
| options.beamStirrupUseHooks | bool | no | true | If true, beam stirrups keep a closed loop and hooks are applied via instance parameters (best-effort). |
| options.beamStirrupHookAngleDeg | number | no | 0 | Hook angle in degrees (e.g. `135`). If omitted and hooks are enabled, defaults to `135` (best-effort). Used to find a matching `RebarHookType` when `beamStirrupHookTypeName` is not provided. |
| options.beamStirrupHookTypeName | string | no |  | Optional exact `RebarHookType` name (preferred over angle). |
| options.beamStirrupHookOrientationStart | string | no | `left` | `left`/`right` (Revit hook orientation). |
| options.beamStirrupHookOrientationEnd | string | no | `right` | `left`/`right` (Revit hook orientation). |
| options.beamStirrupHookStartRotationDeg | number | no | 0 | Hook rotation at start (degrees). |
| options.beamStirrupHookEndRotationDeg | number | no | 179.9 | Hook rotation at end (degrees). |
| options.beamStirrupStartOffsetMm | number | no | 0 | Start offset from beam start face (mm). |
| options.beamStirrupEndOffsetMm | number | no | 0 | End offset from beam end face (mm). |
| options.columnMainBarBottomExtensionMm | number | no | 0 | Column main bar bottom extension (mm). Positive extends; negative shortens. |
| options.columnMainBarTopExtensionMm | number | no | 0 | Column main bar top extension (mm). Positive extends; negative shortens. |
| options.columnMainBarStartBendLengthMm | number | no | 0 | Column main bar start 90-degree bend length (mm). |
| options.columnMainBarEndBendLengthMm | number | no | 0 | Column main bar end 90-degree bend length (mm). |
| options.columnMainBarStartBendDir | string | no | `none` | `up`/`down`/`none` (relative to world Z). |
| options.columnMainBarEndBendDir | string | no | `none` | `up`/`down`/`none` (relative to world Z). |
| options.columnMainBarsPerFace | int | no | 2 | Column perimeter bars per face (>=2). `2` means corners only; `5` yields 16 bars for a rectangular column (example). |
| options.columnTieBottomOffsetMm | number | no | 0 | Bottom offset from column bottom face for first tie (mm). |
| options.columnTieTopOffsetMm | number | no | 0 | Top offset from column top face for last tie (mm). |
| options.columnTieUseHooks | bool | no | true | If true, column ties keep a closed loop and hooks are applied via instance parameters (best-effort). |
| options.columnTieHookAngleDeg | number | no | 0 | Hook angle in degrees (e.g. `135`). If omitted and hooks are enabled, defaults to `135` (best-effort). Used to find a matching `RebarHookType` when `columnTieHookTypeName` is not provided. |
| options.columnTieHookTypeName | string | no |  | Optional exact `RebarHookType` name (preferred over angle). |
| options.columnTieHookOrientationStart | string | no | `left` | `left`/`right`. |
| options.columnTieHookOrientationEnd | string | no | `right` | `left`/`right`. |
| options.columnTieHookStartRotationDeg | number | no | 0 | Hook rotation at start (degrees). |
| options.columnTieHookEndRotationDeg | number | no | 179.9 | Hook rotation at end (degrees). |
| options.columnTieJointPatternEnabled | bool | no | false | If true, replaces the default column tie layout with a joint pattern around “beam top” (best-effort). |
| options.columnTieJointAboveCount | int | no | 3 | Number of ties above the reference plane (includes the reference tie at 0). |
| options.columnTieJointAbovePitchMm | number | no | 100 | Pitch above (mm). |
| options.columnTieJointBelowCount | int | no | 2 | Number of ties below the reference plane (does not include the reference tie). |
| options.columnTieJointBelowPitchMm | number | no | 150 | Pitch below (mm). |
| options.columnTieJointBeamSearchRangeMm | number | no | 1500 | Beam search range (expands the column bbox in XY, mm). |
| options.columnTieJointBeamXYToleranceMm | number | no | 250 | XY overlap tolerance when matching a beam to the column bbox (mm). |
| options.columnTiePattern | object | no |  | Custom column tie pattern (recommended). Equivalent to mapping `Column.Attr.Tie.PatternJson`. |
| options.hookAutoDetectFromExistingTaggedRebar | bool | no | true | If true and hook is not explicitly specified in options, inherits hook type/orientation from existing tagged rebars in the same host (best-effort). |
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

## `Column.Attr.Tie.PatternJson` Example
Example pattern: reference `beam_top`, then 3 ties @ 100mm above (including the reference tie), and 2 ties @ 150mm below (starting at -150mm).
```json
{
  "reference": { "kind": "beam_top", "offsetMm": 0, "beamSearchRangeMm": 1500, "beamXYToleranceMm": 250 },
  "segments": [
    { "name": "above", "direction": "up", "startOffsetMm": 0, "count": 3, "pitchMm": 100 },
    { "name": "below", "direction": "down", "startOffsetMm": 150, "count": 2, "pitchMm": 150 }
  ]
}
```
