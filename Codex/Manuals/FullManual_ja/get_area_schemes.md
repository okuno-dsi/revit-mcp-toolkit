# get_area_schemes

- カテゴリ: Area
- 目的: このコマンドは『get_area_schemes』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_area_schemes

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| count | int | いいえ/状況による |  |
| nameContains | string | いいえ/状況による |  |
| namesOnly | bool | いいえ/状況による | false |
| skip | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_area_schemes",
  "params": {
    "count": 0,
    "nameContains": "...",
    "namesOnly": false,
    "skip": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- create_area
- get_areas
- get_area_params
- update_area
- move_area
- delete_area
- get_area_boundary_walls
- 