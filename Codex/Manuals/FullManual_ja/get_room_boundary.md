# get_room_boundary

- カテゴリ: Room
- 目的: このコマンドは『get_room_boundary』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_room_boundary

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| boundaryLocation | string | いいえ | `Finish` |
| count | int | いいえ/状況による |  |
| elementId | unknown | いいえ/状況による |  |
| skip | int | いいえ/状況による | 0 |

`boundaryLocation` は `SpatialElementBoundaryOptions.SpatialElementBoundaryLocation` を指定します:
- `Finish`（仕上げ面=内法）
- `Center`（壁芯）
- `CoreCenter`（コア芯/躯体芯）
- `CoreBoundary`（コア境界面）

注意:
- これは **A)** `Room.GetBoundarySegments(...)` による「計算境界」です。
- **B)**「部屋境界線（Room Separation Lines）」そのもの（線要素）を扱う場合は `get_room_boundary_lines_in_view` や `create_*_room_boundary_line` 系を使ってください。
- ユーザー入力が「部屋の境界線」等で曖昧な場合は、A) と B) のどちらを指すか実行前に確認してください（`Manuals/FullManual_ja/spatial_boundary_location.md` 参照）。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_room_boundary",
  "params": {
    "boundaryLocation": "CoreCenter",
    "count": 0,
    "elementId": "...",
    "skip": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- summarize_rooms_by_level
- validate_create_room
- get_rooms
- get_room_params
- set_room_param
- create_room
- delete_room
- 
