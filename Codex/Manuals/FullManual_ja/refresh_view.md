# refresh_view

- カテゴリ: VisualizationOps
- 目的: このコマンドは『refresh_view』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: refresh_view

- パラメータ: なし

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "refresh_view",
  "params": {}
}
```

## 関連コマンド
## 関連コマンド
- clear_conditional_coloring
- regen_and_refresh
- simulate_sunlight
- prepare_sunstudy_view
- create_spatial_volume_overlay
- delete_spatial_volume_overlays
- set_visual_override
- 