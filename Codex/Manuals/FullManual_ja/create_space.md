# create_space

- カテゴリ: Space
- 目的: このコマンドは『create_space』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_space

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| heightMm | number | いいえ/状況による |  |
| name | string | いいえ/状況による |  |
| number | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_space",
  "params": {
    "heightMm": 0.0,
    "name": "...",
    "number": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- get_space_params
- get_spaces
- move_space
- update_space
- get_space_boundary
- get_space_boundary_walls
- get_space_centroid
- 