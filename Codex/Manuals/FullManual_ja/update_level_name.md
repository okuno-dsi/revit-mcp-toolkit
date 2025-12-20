# update_level_name

- カテゴリ: LevelOps
- 目的: このコマンドは『update_level_name』を更新します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: update_level_name

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| levelId | long | いいえ/状況による |  |
| name | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_level_name",
  "params": {
    "levelId": "...",
    "name": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- get_levels
- update_level_elevation
- get_level_parameters
- update_level_parameter
- 