# apply_quick_color_scheme

- カテゴリ: VisualizationOps
- 目的: このコマンドは『apply_quick_color_scheme』を適用します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: apply_quick_color_scheme

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| areaSchemeId | unknown | いいえ/状況による |  |
| autoWorkingView | bool | いいえ/状況による | true |
| bins | int | いいえ/状況による | 7 |
| categoryId | int | いいえ/状況による |  |
| createLegend | bool | いいえ/状況による | true |
| detachViewTemplate | bool | いいえ/状況による | false |
| maxClasses | int | いいえ/状況による | 12 |
| mode | string | いいえ/状況による | qualitative |
| palette | string | いいえ/状況による |  |
| viewId | unknown | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "apply_quick_color_scheme",
  "params": {
    "areaSchemeId": "...",
    "autoWorkingView": false,
    "bins": 0,
    "categoryId": 0,
    "createLegend": false,
    "detachViewTemplate": false,
    "maxClasses": 0,
    "mode": "...",
    "palette": "...",
    "viewId": "..."
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