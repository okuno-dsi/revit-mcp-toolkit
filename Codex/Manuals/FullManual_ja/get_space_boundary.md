# get_space_boundary

- カテゴリ: Space
- 目的: Space の **計算境界**（`GetBoundarySegments`）を mm 座標で取得します。

## 概要
これは **A) 計算境界** のコマンドです（`Manuals/FullManual_ja/spatial_boundary_location.md` 参照）。

## 使い方
- メソッド: get_space_boundary

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---:|:---:|---|
| elementId | int | はい |  |
| boundaryLocation | string | いいえ | `Finish` |
| includeElementInfo | bool | いいえ | false |

`boundaryLocation`: `Finish` / `Center` / `CoreCenter` / `CoreBoundary`

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_space_boundary",
  "params": {
    "elementId": 123456,
    "boundaryLocation": "Finish",
    "includeElementInfo": false
  }
}
```

## 関連コマンド
- delete_space
- get_space_params
- get_spaces
- move_space
- update_space
- get_space_boundary_walls
- get_space_centroid

補足:
- レスポンスには `boundaryLocation` と `units` が含まれます。
