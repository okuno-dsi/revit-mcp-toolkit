# create_level

- カテゴリ: LevelOps
- 目的: このコマンドは『create_level』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_level

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| elevation | number | いいえ/状況による |  |
| name | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_level",
  "params": {
    "elevation": 0.0,
    "name": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- update_level_name
- update_level_elevation
- get_level_parameters
- update_level_parameter
- 