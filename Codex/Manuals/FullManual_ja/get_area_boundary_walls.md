# get_area_boundary_walls

- カテゴリ: Area
- 目的: Area の **計算境界**（`GetBoundarySegments`）に現れる壁の要素ID一覧を返します。

## 概要
これは **A) 計算境界** のコマンドです（`Manuals/FullManual_ja/spatial_boundary_location.md` 参照）。

## 使い方
- メソッド: get_area_boundary_walls

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
  "method": "get_area_boundary_walls",
  "params": { "areaId": 123456, "boundaryLocation": "Finish" }
}
```

## 関連コマンド
- create_area
- get_areas
- get_area_params
- update_area
- move_area
- delete_area
- get_area_centroid
- get_area_boundary

補足:
- レスポンスには `wallIds` と `boundaryLocation` が含まれます。
