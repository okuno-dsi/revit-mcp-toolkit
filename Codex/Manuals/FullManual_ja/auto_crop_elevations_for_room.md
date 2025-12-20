# auto_crop_elevations_for_room

- カテゴリ: ViewOps
- 目的: このコマンドは『auto_crop_elevations_for_room』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: auto_crop_elevations_for_room

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| boundaryLocation | string | いいえ/状況による | Finish |
| clipDepthMm | number | いいえ/状況による | 1200.0 |
| minHeightMm | number | いいえ/状況による | 1500.0 |
| minWidthMm | number | いいえ/状況による | 1500.0 |
| paddingMm | number | いいえ/状況による | 200.0 |
| roomId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "auto_crop_elevations_for_room",
  "params": {
    "boundaryLocation": "...",
    "clipDepthMm": 0.0,
    "minHeightMm": 0.0,
    "minWidthMm": 0.0,
    "paddingMm": 0.0,
    "roomId": 0
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