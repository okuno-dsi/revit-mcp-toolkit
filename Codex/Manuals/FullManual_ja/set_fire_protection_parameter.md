# set_fire_protection_parameter

- カテゴリ: FireProtection
- 目的: このコマンドは『set_fire_protection_parameter』を設定します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: set_fire_protection_parameter

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| elementId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_fire_protection_parameter",
  "params": {
    "elementId": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- create_fire_protection_instance
- move_fire_protection_instance
- delete_fire_protection_instance
- get_fire_protection_parameters
- get_fire_protection_types
- duplicate_fire_protection_type
- delete_fire_protection_type
- 