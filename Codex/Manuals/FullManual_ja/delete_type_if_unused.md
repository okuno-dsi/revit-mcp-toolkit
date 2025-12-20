# delete_type_if_unused

- カテゴリ: TypeOps
- 目的: このコマンドは『delete_type_if_unused』を削除します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: delete_type_if_unused

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| category | string | いいえ/状況による |  |
| deleteInstances | bool | いいえ/状況による | false |
| dryRun | bool | いいえ/状況による | false |
| familyName | string | いいえ/状況による |  |
| force | bool | いいえ/状況による | false |
| purgeAllUnusedInCategory | bool | いいえ/状況による | false |
| reassignToFamilyName | string | いいえ/状況による |  |
| reassignToTypeId | int | いいえ/状況による |  |
| reassignToTypeName | string | いいえ/状況による |  |
| reassignToUniqueId | string | いいえ/状況による |  |
| typeId | int | いいえ/状況による |  |
| typeName | string | いいえ/状況による |  |
| uniqueId | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "delete_type_if_unused",
  "params": {
    "category": "...",
    "deleteInstances": false,
    "dryRun": false,
    "familyName": "...",
    "force": false,
    "purgeAllUnusedInCategory": false,
    "reassignToFamilyName": "...",
    "reassignToTypeId": 0,
    "reassignToTypeName": "...",
    "reassignToUniqueId": "...",
    "typeId": 0,
    "typeName": "...",
    "uniqueId": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- force_delete_type
- rename_types_bulk
- rename_types_by_parameter
- 