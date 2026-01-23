# draw_colored_line_segments

- Category: AnnotationOps
- Canonical: `view.draw_colored_line_segments`
- Legacy: `draw_colored_line_segments`

## Overview
Draw **detail line segments** in a view from a coordinate dataset (mm), and apply color (and optional line weight) via **per-element view overrides**.

- Input is **mm** `{x,y,z}`.
- Color/weight is applied using `View.SetElementOverrides` (it does not modify line styles).
- Accepts `loops[].segments[]` compatible with outputs such as `get_room_perimeter_with_columns` when `includeSegments=true`.

## Parameters
| Name | Type | Required | Default | Notes |
|---|---|---:|---|---|
| `viewId` | int | no | (active view) | Target view id. If omitted, uses current active view. |
| `segments` | object[] | no |  | `[{start:{x,y,z}, end:{x,y,z}, lineRgb?, lineWeight?}, ...]` (mm). |
| `loops` | object[] | no |  | `[{segments:[...]}]` (compatible with room perimeter outputs). |
| `dataset` | object | no |  | Wrapper for `dataset.segments` / `dataset.loops`. |
| `lineRgb` | object | no | `{r:255,g:0,b:0}` | Default line color `{r,g,b}` (0–255). |
| `r`,`g`,`b` | int | no |  | Alternative to `lineRgb`. |
| `lineWeight` | int | no |  | Default projection line weight (1–16). |
| `applyOverrides` | bool | no | `true` | Apply color/weight overrides if `true`. |
| `detachViewTemplate` | bool | no | `false` | Detach view template before drawing (modifies the model). |
| `batchSize` | int | no | `500` | Batch size for large datasets. |
| `startIndex` | int | no | `0` | Resume index. |
| `maxMillisPerTx` | int | no | `3000` | Soft cap per transaction (ms). |
| `refreshView` | bool | no | `true` | Try `Regenerate/RefreshActiveView` after creation. |
| `returnIds` | bool | no | `true` | Return `createdElementIds` when `true`. |

## View template note
If the target view has a view template and `applyOverrides=true`, the command returns `errorCode=VIEW_TEMPLATE_LOCK` and does not create any lines (unless `detachViewTemplate=true`).

## Example: draw room perimeter loops in red
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view.draw_colored_line_segments",
  "params": {
    "viewId": 6781038,
    "loops": [
      { "loopIndex": 0, "segments": [ { "start": {"x":0,"y":0,"z":0}, "end": {"x":1000,"y":0,"z":0} } ] }
    ],
    "lineRgb": { "r": 255, "g": 0, "b": 0 },
    "lineWeight": 6
  }
}
```

