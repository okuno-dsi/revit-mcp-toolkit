# check_fire_rating_compliance

- カテゴリ: FireProtection
- 目的: このコマンドは『check_fire_rating_compliance』を検査します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: check_fire_rating_compliance

- パラメータ: なし

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "check_fire_rating_compliance",
  "params": {}
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
- duplicate_fire_protection_type
- 