# get_room_params

- カテゴリ: Room
- 目的: このコマンドは『get_room_params』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_room_params

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| count | int | いいえ/状況による |  |
| roomId | unknown | いいえ/状況による |  |
| skip | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_room_params",
  "params": {
    "count": 0,
    "roomId": "...",
    "skip": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- summarize_rooms_by_level
- validate_create_room
- get_rooms
- set_room_param
- get_room_boundary
- create_room
- delete_room
- 