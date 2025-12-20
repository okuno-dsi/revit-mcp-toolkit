# delete_spatial_volume_overlays

- カテゴリ: VisualizationOps
- 目的: このコマンドは『delete_spatial_volume_overlays』を削除します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: delete_spatial_volume_overlays

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| all | bool | いいえ/状況による | false |
| dryRun | bool | いいえ/状況による | false |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "delete_spatial_volume_overlays",
  "params": {
    "all": false,
    "dryRun": false
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
- set_visual_override
- 