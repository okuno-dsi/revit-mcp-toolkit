# get_space_params

- カテゴリ: Space
- 目的: このコマンドは『get_space_params』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_space_params

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| count | int | いいえ/状況による |  |
| elementId | unknown | いいえ/状況による |  |
| skip | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_space_params",
  "params": {
    "count": 0,
    "elementId": "...",
    "skip": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- delete_space
- get_spaces
- move_space
- update_space
- get_space_boundary
- get_space_boundary_walls
- get_space_centroid
- 