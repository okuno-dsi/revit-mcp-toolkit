# get_revision_cloud_types

- カテゴリ: RevisionCloud
- 目的: このコマンドは『get_revision_cloud_types』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_revision_cloud_types

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| count | int | いいえ/状況による |  |
| nameContains | string | いいえ/状況による |  |
| skip | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_revision_cloud_types",
  "params": {
    "count": 0,
    "nameContains": "...",
    "skip": 0
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
- get_revision_cloud_type_parameters
- set_revision_cloud_type_parameter
- 