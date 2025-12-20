# adjust_grid_extents

- カテゴリ: GridOps
- 目的: このコマンドは『adjust_grid_extents』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: adjust_grid_extents

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| detachScopeBoxForAdjustment | bool | いいえ/状況による | false |
| dryRun | bool | いいえ/状況による | false |
| includeLinkedModels | bool | いいえ/状況による | false |
| mode | string | いいえ/状況による | both |
| skipPinned | bool | いいえ/状況による | true |
| viewFilter | unknown | いいえ/状況による |  |
| viewIds | unknown | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "adjust_grid_extents",
  "params": {
    "detachScopeBoxForAdjustment": false,
    "dryRun": false,
    "includeLinkedModels": false,
    "mode": "...",
    "skipPinned": false,
    "viewFilter": "...",
    "viewIds": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- create_grids
- update_grid_name
- move_grid
- delete_grid
- 