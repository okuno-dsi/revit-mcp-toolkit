# create_revision_cloud

- カテゴリ: RevisionCloud
- 目的: このコマンドは『create_revision_cloud』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_revision_cloud

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| revisionId | int | いいえ/状況による |  |
| viewId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_revision_cloud",
  "params": {
    "revisionId": 0,
    "viewId": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- create_revision_circle
- move_revision_cloud
- delete_revision_cloud
- update_revision
- get_revision_cloud_types
- get_revision_cloud_type_parameters
- set_revision_cloud_type_parameter
- 