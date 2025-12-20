# get_areas

- カテゴリ: Area
- 目的: このコマンドは『get_areas』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_areas

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| areaMaxM2 | number | いいえ/状況による |  |
| areaMinM2 | number | いいえ/状況による |  |
| count | int | いいえ/状況による |  |
| desc | bool | いいえ/状況による | false |
| includeCentroid | bool | いいえ/状況による | false |
| includeParameters | bool | いいえ/状況による | false |
| levelId | int | いいえ/状況による |  |
| nameContains | string | いいえ/状況による |  |
| numberContains | string | いいえ/状況による |  |
| orderBy | string | いいえ/状況による | id |
| skip | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_areas",
  "params": {
    "areaMaxM2": 0.0,
    "areaMinM2": 0.0,
    "count": 0,
    "desc": false,
    "includeCentroid": false,
    "includeParameters": false,
    "levelId": 0,
    "nameContains": "...",
    "numberContains": "...",
    "orderBy": "...",
    "skip": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- create_area
- get_area_params
- update_area
- move_area
- delete_area
- get_area_boundary_walls
- get_area_centroid
- 