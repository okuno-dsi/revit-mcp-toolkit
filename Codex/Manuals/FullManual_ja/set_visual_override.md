# set_visual_override

- カテゴリ: VisualizationOps
- 目的: このコマンドは『set_visual_override』を設定します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: set_visual_override

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| autoWorkingView | bool | いいえ/状況による | true |
| b | int | いいえ/状況による | 0 |
| batchSize | int | いいえ/状況による | 800 |
| detachViewTemplate | bool | いいえ/状況による | false |
| g | int | いいえ/状況による | 0 |
| lineRgb | object | いいえ/状況による |  | 線色を `{r,g,b}`（0-255）で指定（任意）。未指定なら `r/g/b` を使用。 |
| maxMillisPerTx | int | いいえ/状況による | 3000 |
| r | int | いいえ/状況による | 255 |
| fillRgb | object | いいえ/状況による |  | 塗りつぶし（Surface/Cut パターン色）を `{r,g,b}`（0-255）で指定（任意）。未指定なら `r/g/b` を使用。 |
| refreshView | bool | いいえ/状況による | true |
| startIndex | int | いいえ/状況による | 0 |
| transparency | int | いいえ/状況による | 40 |
| viewId | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_visual_override",
  "params": {
    "autoWorkingView": false,
    "b": 0,
    "batchSize": 0,
    "detachViewTemplate": false,
    "g": 0,
    "maxMillisPerTx": 0,
    "r": 0,
    "refreshView": false,
    "startIndex": 0,
    "transparency": 0,
    "viewId": 0
  }
}
```

## 備考

- このコマンドは要素に対して「線色・塗りつぶしパターン・透過度」を強制的に上書きします。  
  ビューの**視覚スタイル**が「陰線処理（Hidden Line）」以外の場合、塗りつぶしが期待どおりに見えないことがあります。  
  その場合は、ビューの視覚スタイルを「陰線処理」や「ワイヤーフレーム」に変更して確認してください。
- 上書きを元に戻す、いわゆる「リセット専用」のコマンドはありません。  
  個々の要素については Revit の UI から「要素グラフィックスの上書きをリセット」を行うか、
  別の `set_visual_override` 呼び出しで色や透過度を再設定してください。

## 関連コマンド
- clear_conditional_coloring
- refresh_view
- regen_and_refresh
- simulate_sunlight
- prepare_sunstudy_view
- create_spatial_volume_overlay
- delete_spatial_volume_overlays
