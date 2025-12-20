# get_room_boundary_walls

- カテゴリ: Room
- 目的: このコマンドは『get_room_boundary_walls』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_room_boundary_walls

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| boundaryLocation | string | いいえ | `Finish` |
| elementId | unknown | いいえ/状況による |  |

`boundaryLocation` は `SpatialElementBoundaryOptions.SpatialElementBoundaryLocation` を指定します（値は `get_room_boundary` と同様）。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_room_boundary_walls",
  "params": {
    "boundaryLocation": "CoreCenter",
    "elementId": "..."
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
- get_room_boundary
- create_room
- 
