# create_revision_circle

- カテゴリ: RevisionCloud
- 目的: このコマンドは『create_revision_circle』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_revision_circle

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| radiusMm | number | いいえ/状況による | 0.0 |
| revisionId | int | いいえ/状況による | 0 |
| segments | int | いいえ/状況による | 24 |
| viewId | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_revision_circle",
  "params": {
    "radiusMm": 0.0,
    "revisionId": 0,
    "segments": 0,
    "viewId": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- create_default_revision
- move_revision_cloud
- delete_revision_cloud
- update_revision
- get_revision_cloud_types
- get_revision_cloud_type_parameters
- set_revision_cloud_type_parameter
- 