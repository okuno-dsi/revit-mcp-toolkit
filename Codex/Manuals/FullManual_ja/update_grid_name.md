# update_grid_name

- カテゴリ: GridOps
- 目的: このコマンドは『update_grid_name』を更新します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: update_grid_name

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| name | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_grid_name",
  "params": {
    "name": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- create_grids
- move_grid
- delete_grid
- adjust_grid_extents
- 