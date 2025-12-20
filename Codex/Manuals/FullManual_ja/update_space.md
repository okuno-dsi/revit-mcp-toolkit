# update_space

- カテゴリ: Space
- 目的: このコマンドは『update_space』を更新します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: update_space

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| elementId | unknown | いいえ/状況による |  |
| paramName | unknown | いいえ/状況による |  |
| value | unknown | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_space",
  "params": {
    "elementId": "...",
    "paramName": "...",
    "value": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- delete_space
- get_space_params
- get_spaces
- move_space
- get_space_boundary
- get_space_boundary_walls
- get_space_centroid
- 