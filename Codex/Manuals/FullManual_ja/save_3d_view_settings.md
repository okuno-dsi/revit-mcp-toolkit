# save_3d_view_settings

- カテゴリ: ViewOps
- 目的: このコマンドは『save_3d_view_settings』を保存します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: save_3d_view_settings

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| deriveCropFromViewExtents | bool | いいえ/状況による | true |
| viewId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "save_3d_view_settings",
  "params": {
    "deriveCropFromViewExtents": false,
    "viewId": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- get_view_info
- save_view_state
- restore_view_state
- create_view_plan
- create_section
- create_elevation_view
- compare_view_states
- 