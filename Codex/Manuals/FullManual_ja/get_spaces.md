# get_spaces

- カテゴリ: Space
- 目的: このコマンドは『get_spaces』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_spaces

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| count | int | いいえ/状況による |  |
| desc | bool | いいえ/状況による | false |
| includeCenter | bool | いいえ/状況による | true |
| includeParameters | bool | いいえ/状況による | false |
| levelId | int | いいえ/状況による |  |
| nameContains | string | いいえ/状況による |  |
| numberContains | string | いいえ/状況による |  |
| orderBy | string | いいえ/状況による | number |
| skip | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_spaces",
  "params": {
    "count": 0,
    "desc": false,
    "includeCenter": false,
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
- delete_space
- get_space_params
- move_space
- update_space
- get_space_boundary
- get_space_boundary_walls
- get_space_centroid
- 