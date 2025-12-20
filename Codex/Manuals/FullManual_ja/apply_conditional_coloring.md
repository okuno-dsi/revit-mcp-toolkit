# apply_conditional_coloring

- カテゴリ: VisualizationOps
- 目的: このコマンドは『apply_conditional_coloring』を適用します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: apply_conditional_coloring

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| autoWorkingView | bool | いいえ/状況による | true |
| batchSize | int | いいえ/状況による | 800 |
| detachViewTemplate | bool | いいえ/状況による | false |
| maxMillisPerTx | int | いいえ/状況による | 3000 |
| mode | string | いいえ/状況による | by_element_ranges |
| refreshView | bool | いいえ/状況による | true |
| startIndex | int | いいえ/状況による | 0 |
| units | string | いいえ/状況による | auto |
| viewId | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "apply_conditional_coloring",
  "params": {
    "autoWorkingView": false,
    "batchSize": 0,
    "detachViewTemplate": false,
    "maxMillisPerTx": 0,
    "mode": "...",
    "refreshView": false,
    "startIndex": 0,
    "units": "...",
    "viewId": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- refresh_view
- regen_and_refresh
- simulate_sunlight
- prepare_sunstudy_view
- create_spatial_volume_overlay
- delete_spatial_volume_overlays
- set_visual_override
- 