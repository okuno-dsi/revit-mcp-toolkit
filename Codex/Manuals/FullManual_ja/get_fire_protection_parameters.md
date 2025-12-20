# get_fire_protection_parameters

- カテゴリ: FireProtection
- 目的: このコマンドは『get_fire_protection_parameters』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_fire_protection_parameters

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| elementId | int | いいえ/状況による |  |
| includeDisplay | bool | いいえ/状況による | true |
| includeRaw | bool | いいえ/状況による | true |
| includeUnit | bool | いいえ/状況による | true |
| siDigits | int | いいえ/状況による | 3 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_fire_protection_parameters",
  "params": {
    "elementId": 0,
    "includeDisplay": false,
    "includeRaw": false,
    "includeUnit": false,
    "siDigits": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- create_fire_protection_instance
- move_fire_protection_instance
- delete_fire_protection_instance
- set_fire_protection_parameter
- get_fire_protection_types
- duplicate_fire_protection_type
- delete_fire_protection_type
- 