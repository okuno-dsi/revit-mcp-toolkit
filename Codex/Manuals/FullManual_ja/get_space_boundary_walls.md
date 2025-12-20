# get_space_boundary_walls

- カテゴリ: Space
- 目的: Space の **計算境界**（`GetBoundarySegments`）に現れる壁の要素ID一覧を返します。

## 概要
これは **A) 計算境界** のコマンドです（`Manuals/FullManual_ja/spatial_boundary_location.md` 参照）。

## 使い方
- メソッド: get_space_boundary_walls

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---:|:---:|---|
| elementId | int | はい |  |
| boundaryLocation | string | いいえ | `Finish` |

`boundaryLocation`: `Finish` / `Center` / `CoreCenter` / `CoreBoundary`

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_space_boundary_walls",
  "params": {
    "elementId": 123456,
    "boundaryLocation": "Finish"
  }
}
```

## 関連コマンド
- delete_space
- get_space_params
- get_spaces
- move_space
- update_space
- get_space_boundary
- get_space_centroid

補足:
- レスポンスには `boundaryLocation` と `units` が含まれます。
