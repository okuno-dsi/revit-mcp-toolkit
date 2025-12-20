# create_zone

- カテゴリ: ZoneOps
- 目的: このコマンドは『create_zone』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_zone

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| levelId | int | いいえ/状況による |  |
| levelName | string | いいえ/状況による |  |
| name | string | いいえ/状況による |  |
| number | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_zone",
  "params": {
    "levelId": 0,
    "levelName": "...",
    "name": "...",
    "number": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- add_spaces_to_zone
- remove_spaces_from_zone
- delete_zone
- list_zone_members
- get_zone_params
- set_zone_param
- compute_zone_metrics
- 