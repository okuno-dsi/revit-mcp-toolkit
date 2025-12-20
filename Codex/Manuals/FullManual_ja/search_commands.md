# search_commands

- カテゴリ: MetaOps
- 目的: このコマンドは『search_commands』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: search_commands

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| q | unknown | はい |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "search_commands",
  "params": {
    "q": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- stop_command_logging
- agent_bootstrap
- 