# compute_zone_metrics

- カテゴリ: ZoneOps
- 目的: このコマンドは『compute_zone_metrics』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: compute_zone_metrics

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| zoneId | int | いいえ/状況による |  |
| zoneUniqueId | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "compute_zone_metrics",
  "params": {
    "zoneId": 0,
    "zoneUniqueId": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- create_zone
- add_spaces_to_zone
- remove_spaces_from_zone
- delete_zone
- list_zone_members
- get_zone_params
- set_zone_param
- 