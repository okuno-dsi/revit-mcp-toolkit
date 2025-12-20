# list_revisions

- カテゴリ: RevisionCloud
- 目的: このコマンドは『list_revisions』を一覧取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: list_revisions

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| includeClouds | bool | いいえ/状況による | false |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "list_revisions",
  "params": {
    "includeClouds": false
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