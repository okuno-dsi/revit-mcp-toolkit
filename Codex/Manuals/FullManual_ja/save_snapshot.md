# save_snapshot

- カテゴリ: DocumentOps
- 目的: このコマンドは『save_snapshot』を保存します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: save_snapshot

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| autoTimestamp | bool | いいえ/状況による | true |
| baseName | string | いいえ/状況による |  |
| dir | string | いいえ/状況による |  |
| prefix | string | いいえ/状況による |  |
| timestampFormat | string | いいえ/状況による | yyyyMMddHHmmss |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "save_snapshot",
  "params": {
    "autoTimestamp": false,
    "baseName": "...",
    "dir": "...",
    "prefix": "...",
    "timestampFormat": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- get_project_categories
- get_project_summary
- 