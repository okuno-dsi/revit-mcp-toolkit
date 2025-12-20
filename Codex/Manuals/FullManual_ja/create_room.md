# create_room

- カテゴリ: Room
- 目的: このコマンドは『create_room』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_room

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| autoTag | bool | いいえ/状況による | true |
| checkExisting | bool | いいえ/状況による | true |
| levelId | int | いいえ/状況による |  |
| levelName | string | いいえ/状況による |  |
| minAreaMm2 | number | いいえ/状況による | 1 |
| strictEnclosure | bool | いいえ/状況による | true |
| tagTypeId | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_room",
  "params": {
    "autoTag": false,
    "checkExisting": false,
    "levelId": 0,
    "levelName": "...",
    "minAreaMm2": 0.0,
    "strictEnclosure": false,
    "tagTypeId": 0
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
- delete_room
- 