# set_area_volume_settings

- カテゴリ: Room
- 目的: このコマンドは『set_area_volume_settings』を設定します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: set_area_volume_settings

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| computeVolumes | bool | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_area_volume_settings",
  "params": {
    "computeVolumes": false
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