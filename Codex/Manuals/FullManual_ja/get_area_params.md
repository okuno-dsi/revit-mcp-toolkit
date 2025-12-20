# get_area_params

- カテゴリ: Area
- 目的: このコマンドは『get_area_params』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_area_params

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| areaId | unknown | いいえ/状況による |  |
| count | int | いいえ/状況による |  |
| desc | bool | いいえ/状況による | false |
| includeDisplay | bool | いいえ/状況による | true |
| includeRaw | bool | いいえ/状況による | true |
| includeUnit | bool | いいえ/状況による | true |
| nameContains | string | いいえ/状況による |  |
| orderBy | string | いいえ/状況による | name |
| skip | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_area_params",
  "params": {
    "areaId": "...",
    "count": 0,
    "desc": false,
    "includeDisplay": false,
    "includeRaw": false,
    "includeUnit": false,
    "nameContains": "...",
    "orderBy": "...",
    "skip": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- create_area
- get_areas
- update_area
- move_area
- delete_area
- get_area_boundary_walls
- get_area_centroid
- 