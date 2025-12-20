# update_revision

- カテゴリ: RevisionCloud
- 目的: このコマンドは『update_revision』を更新します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: update_revision

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| revisionId | int | いいえ/状況による | 0 |
| uniqueId | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_revision",
  "params": {
    "revisionId": 0,
    "uniqueId": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- create_default_revision
- create_revision_circle
- move_revision_cloud
- delete_revision_cloud
- get_revision_cloud_types
- get_revision_cloud_type_parameters
- set_revision_cloud_type_parameter
- 