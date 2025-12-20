# clear_visual_override

- カテゴリ: VisualizationOps
- 目的: このコマンドは『clear_visual_override』をクリアします。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: clear_visual_override

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| autoWorkingView | bool | いいえ/状況による | true |
| batchSize | int | いいえ/状況による | 800 |
| detachViewTemplate | bool | いいえ/状況による | false |
| maxMillisPerTx | int | いいえ/状況による | 3000 |
| refreshView | bool | いいえ/状況による | true |
| startIndex | int | いいえ/状況による | 0 |
| viewId | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "clear_visual_override",
  "params": {
    "autoWorkingView": false,
    "batchSize": 0,
    "detachViewTemplate": false,
    "maxMillisPerTx": 0,
    "refreshView": false,
    "startIndex": 0,
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