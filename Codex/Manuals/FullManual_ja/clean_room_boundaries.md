# clean_room_boundaries

- カテゴリ: Room
- 目的: このコマンドは『clean_room_boundaries』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: clean_room_boundaries

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| deleteIsolated | bool | いいえ/状況による | false |
| extendToleranceMm | number | いいえ/状況による | 50.0 |
| mergeToleranceMm | number | いいえ/状況による | 5.0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "clean_room_boundaries",
  "params": {
    "deleteIsolated": false,
    "extendToleranceMm": 0.0,
    "mergeToleranceMm": 0.0
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