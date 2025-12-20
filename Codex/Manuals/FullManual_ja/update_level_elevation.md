# update_level_elevation

- カテゴリ: LevelOps
- 目的: このコマンドは『update_level_elevation』を更新します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: update_level_elevation

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| elevation | number | いいえ/状況による |  |
| levelId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_level_elevation",
  "params": {
    "elevation": 0.0,
    "levelId": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- get_levels
- update_level_name
- get_level_parameters
- update_level_parameter
- 