# get_space_centroid

- カテゴリ: Space
- 目的: このコマンドは『get_space_centroid』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_space_centroid

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| elementId | unknown | いいえ/状況による |  |
| includeBoundingBox | bool | いいえ/状況による | false |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_space_centroid",
  "params": {
    "elementId": "...",
    "includeBoundingBox": false
  }
}
```

## 関連コマンド
## 関連コマンド
- delete_space
- get_space_params
- get_spaces
- move_space
- update_space
- get_space_boundary
- get_space_boundary_walls
- 