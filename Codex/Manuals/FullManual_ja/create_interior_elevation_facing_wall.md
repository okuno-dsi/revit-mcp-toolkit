# create_interior_elevation_facing_wall

- カテゴリ: ViewOps
- 目的: このコマンドは『create_interior_elevation_facing_wall』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_interior_elevation_facing_wall

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| autoCrop | unknown | いいえ/状況による |  |
| hostViewId | int | いいえ/状況による | 0 |
| levelId | int | いいえ/状況による |  |
| levelName | string | いいえ/状況による |  |
| name | string | いいえ/状況による |  |
| offsetMm | number | いいえ/状況による | 800.0 |
| roomId | unknown | いいえ/状況による |  |
| roomUniqueId | unknown | いいえ/状況による |  |
| scale | int | いいえ/状況による | 100 |
| viewTypeId | int | いいえ/状況による | 0 |
| wallId | unknown | いいえ/状況による |  |
| wallUniqueId | unknown | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_interior_elevation_facing_wall",
  "params": {
    "autoCrop": "...",
    "hostViewId": 0,
    "levelId": 0,
    "levelName": "...",
    "name": "...",
    "offsetMm": 0.0,
    "roomId": "...",
    "roomUniqueId": "...",
    "scale": 0,
    "viewTypeId": 0,
    "wallId": "...",
    "wallUniqueId": "..."
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