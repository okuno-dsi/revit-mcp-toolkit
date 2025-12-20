# create_room_interior_elevations

- カテゴリ: ViewOps
- 目的: このコマンドは『create_room_interior_elevations』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_room_interior_elevations

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| namePrefix | string | いいえ/状況による |  |
| offsetMm | number | いいえ/状況による | 1000.0 |
| roomId | int | いいえ/状況による |  |
| scale | int | いいえ/状況による | 100 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_room_interior_elevations",
  "params": {
    "namePrefix": "...",
    "offsetMm": 0.0,
    "roomId": 0,
    "scale": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- get_view_info
- save_view_state
- restore_view_state
- create_view_plan
- create_section
- create_elevation_view
- compare_view_states
- 