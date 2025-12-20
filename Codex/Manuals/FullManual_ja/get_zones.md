# get_zones

- カテゴリ: ZoneOps
- 目的: このコマンドは『get_zones』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_zones

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| count | int | いいえ/状況による | 100 |
| levelId | int | いいえ/状況による |  |
| levelName | string | いいえ/状況による |  |
| nameContains | string | いいえ/状況による |  |
| namesOnly | bool | いいえ/状況による | false |
| numberContains | string | いいえ/状況による |  |
| skip | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_zones",
  "params": {
    "count": 0,
    "levelId": 0,
    "levelName": "...",
    "nameContains": "...",
    "namesOnly": false,
    "numberContains": "...",
    "skip": 0
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