# list_color_schemes

- カテゴリ: VisualizationOps
- 目的: このコマンドは『list_color_schemes』を一覧取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: list_color_schemes

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| areaSchemeId | unknown | いいえ/状況による |  |
| categoryId | unknown | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "list_color_schemes",
  "params": {
    "areaSchemeId": "...",
    "categoryId": "..."
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