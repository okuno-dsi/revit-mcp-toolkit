# create_spatial_volume_overlay

- カテゴリ: VisualizationOps
- 目的: このコマンドは『create_spatial_volume_overlay』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_spatial_volume_overlay

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| batchSize | int | いいえ/状況による | 200 |
| maxMillisPerTx | int | いいえ/状況による | 3000 |
| refreshView | bool | いいえ/状況による | true |
| startIndex | int | いいえ/状況による | 0 |
| target | string | いいえ/状況による | room |
| transparency | int | いいえ/状況による | 60 |
| viewId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_spatial_volume_overlay",
  "params": {
    "batchSize": 0,
    "maxMillisPerTx": 0,
    "refreshView": false,
    "startIndex": 0,
    "target": "...",
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
- delete_spatial_volume_overlays
- set_visual_override
- 