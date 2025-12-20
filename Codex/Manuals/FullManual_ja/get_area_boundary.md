# get_area_boundary

- カテゴリ: Area
- 目的: Area の **計算境界**（`GetBoundarySegments`）を mm 座標で取得します。

## 概要
これは **A) 計算境界** のコマンドです（`Manuals/FullManual_ja/spatial_boundary_location.md` 参照）。
エリア境界線（Area Boundary Lines）の線要素そのものを操作したい場合は `*_area_boundary_line` 系を使用してください。

## 使い方
- メソッド: get_area_boundary

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---:|:---:|---|
| areaId | int | はい |  |
| boundaryLocation | string | いいえ | `Finish` |

`boundaryLocation` は `SpatialElementBoundaryOptions.SpatialElementBoundaryLocation` を指定します:
`Finish` / `Center` / `CoreCenter` / `CoreBoundary`

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_area_boundary",
  "params": { "areaId": 123456, "boundaryLocation": "CoreCenter" }
}
```

## 関連コマンド
- get_areas
- get_area_params
- update_area
- move_area
- delete_area
- get_area_boundary_walls
- get_area_centroid
- get_area_boundary_lines_in_view
- create_area_boundary_line
- delete_area_boundary_line
- move_area_boundary_line
- trim_area_boundary_line
- extend_area_boundary_line

補足:
- レスポンスには `loops[].points`（始点列）と `loops[].segments`（始点/終点）が含まれます。
