# get_levels

- カテゴリ: LevelOps
- 目的: このコマンドは『get_levels』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_levels

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| count | int | いいえ/状況による |  |
| skip | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_levels",
  "params": {
    "count": 0,
    "skip": 0
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