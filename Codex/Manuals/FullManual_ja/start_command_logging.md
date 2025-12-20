# start_command_logging

- カテゴリ: MetaOps
- 目的: このコマンドは『start_command_logging』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: start_command_logging

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| dir | string | いいえ/状況による |  |
| prefix | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "start_command_logging",
  "params": {
    "dir": "...",
    "prefix": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- stop_command_logging
- agent_bootstrap
- 