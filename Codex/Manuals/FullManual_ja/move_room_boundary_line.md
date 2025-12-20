# move_room_boundary_line

- カテゴリ: Room
- 目的: このコマンドは『move_room_boundary_line』を移動します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: move_room_boundary_line

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| dx | number | いいえ/状況による | 0.0 |
| dy | number | いいえ/状況による | 0.0 |
| dz | number | いいえ/状況による | 0.0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "move_room_boundary_line",
  "params": {
    "dx": 0.0,
    "dy": 0.0,
    "dz": 0.0
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