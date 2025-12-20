# prepare_sunstudy_view

- カテゴリ: VisualizationOps
- 目的: このコマンドは『prepare_sunstudy_view』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: prepare_sunstudy_view

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| baseName | string | いいえ/状況による | SunStudy 3D |
| detailLevel | string | いいえ/状況による | Fine |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "prepare_sunstudy_view",
  "params": {
    "baseName": "...",
    "detailLevel": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- clear_conditional_coloring
- refresh_view
- regen_and_refresh
- simulate_sunlight
- create_spatial_volume_overlay
- delete_spatial_volume_overlays
- set_visual_override
- 