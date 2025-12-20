# duplicate_fire_protection_type

- カテゴリ: FireProtection
- 目的: このコマンドは『duplicate_fire_protection_type』を複製します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: duplicate_fire_protection_type

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| newTypeName | string | いいえ/状況による |  |
| sourceTypeId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "duplicate_fire_protection_type",
  "params": {
    "newTypeName": "...",
    "sourceTypeId": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- create_fire_protection_instance
- move_fire_protection_instance
- delete_fire_protection_instance
- get_fire_protection_parameters
- set_fire_protection_parameter
- get_fire_protection_types
- delete_fire_protection_type
- 