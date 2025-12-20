# create_obb_cloud_for_selection

- カテゴリ: RevisionCloud
- 目的: このコマンドは『create_obb_cloud_for_selection』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_obb_cloud_for_selection

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| ensureCloudVisible | bool | いいえ/状況による | true |
| paddingMm | number | いいえ/状況による |  |
| widthMm | number | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_obb_cloud_for_selection",
  "params": {
    "ensureCloudVisible": false,
    "paddingMm": 0.0,
    "widthMm": 0.0
  }
}
```

## 関連コマンド
## 関連コマンド
- create_default_revision
- create_revision_circle
- move_revision_cloud
- delete_revision_cloud
- update_revision
- get_revision_cloud_types
- get_revision_cloud_type_parameters
- 