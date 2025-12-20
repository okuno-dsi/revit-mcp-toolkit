# apply_3d_view_settings

- カテゴリ: ViewOps
- 目的: このコマンドは『apply_3d_view_settings』を適用します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: apply_3d_view_settings

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| allowPerspectiveCrop | bool | いいえ/状況による | false |
| createNewViewOnTypeMismatch | bool | いいえ/状況による | true |
| cropActive | bool | いいえ/状況による |  |
| cropVisible | bool | いいえ/状況による |  |
| fitOnApply | bool | いいえ/状況による | true |
| isPerspective | bool | いいえ/状況による |  |
| sectionActive | bool | いいえ/状況による |  |
| snapCropToView | bool | いいえ/状況による | true |
| switchActive | bool | いいえ/状況による | false |
| viewId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "apply_3d_view_settings",
  "params": {
    "allowPerspectiveCrop": false,
    "createNewViewOnTypeMismatch": false,
    "cropActive": false,
    "cropVisible": false,
    "fitOnApply": false,
    "isPerspective": false,
    "sectionActive": false,
    "snapCropToView": false,
    "switchActive": false,
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