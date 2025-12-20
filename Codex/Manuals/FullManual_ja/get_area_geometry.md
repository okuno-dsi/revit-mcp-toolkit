# get_area_geometry

- カテゴリ: Area
- 目的: Area の **計算境界**（`GetBoundarySegments`）をセグメントとして mm 座標で取得します。

## 概要
これは **A) 計算境界** のコマンドです（`Manuals/FullManual_ja/spatial_boundary_location.md` 参照）。

## 使い方
- メソッド: get_area_geometry

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---:|:---:|---|
| areaId | int | はい |  |
| boundaryLocation | string | いいえ | `Finish` |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_area_geometry",
  "params": { "areaId": 123456, "boundaryLocation": "CoreCenter" }
}
```

## 関連コマンド
- create_area
- get_areas
- get_area_params
- update_area
- move_area
- delete_area
- get_area_boundary_walls
- get_area_boundary

補足:
- レスポンスには `boundaryLocation` と `geometry`（ループ配列）が含まれます。
