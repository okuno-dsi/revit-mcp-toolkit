# batch_set_visual_override

- カテゴリ: VisualizationOps
- 目的: このコマンドは『batch_set_visual_override』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: batch_set_visual_override

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| b | int | いいえ/状況による | 80 |
| detachViewTemplate | bool | いいえ/状況による | true |
| g | int | いいえ/状況による | 230 |
| r | int | いいえ/状況による | 200 |
| transparency | int | いいえ/状況による | 40 |
| viewId | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "batch_set_visual_override",
  "params": {
    "b": 0,
    "detachViewTemplate": false,
    "g": 0,
    "r": 0,
    "transparency": 0,
    "viewId": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- clear_conditional_coloring
- refresh_view
- regen_and_refresh
- simulate_sunlight
- prepare_sunstudy_view
- create_spatial_volume_overlay
- delete_spatial_volume_overlays
- 