# get_zone_params

- カテゴリ: ZoneOps
- 目的: このコマンドは『get_zone_params』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_zone_params

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| count | int | いいえ/状況による | 100 |
| desc | bool | いいえ/状況による | false |
| includeDisplay | bool | いいえ/状況による | true |
| includeRaw | bool | いいえ/状況による | true |
| includeUnit | bool | いいえ/状況による | true |
| nameContains | string | いいえ/状況による |  |
| orderBy | string | いいえ/状況による |  |
| skip | int | いいえ/状況による | 0 |
| zoneId | int | いいえ/状況による |  |
| zoneUniqueId | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_zone_params",
  "params": {
    "count": 0,
    "desc": false,
    "includeDisplay": false,
    "includeRaw": false,
    "includeUnit": false,
    "nameContains": "...",
    "orderBy": "...",
    "skip": 0,
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
- set_zone_param
- compute_zone_metrics
- 