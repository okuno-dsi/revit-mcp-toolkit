# analyze_segments

- カテゴリ: Commands
- 目的: このコマンドは『analyze_segments』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: analyze_segments

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| mode | string | いいえ/状況による | 2d |
| point | object | いいえ/状況による |  |
| seg1 | object | いいえ/状況による |  |
| seg2 | object | いいえ/状況による |  |
| tol | object | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "analyze_segments",
  "params": {
    "mode": "...",
    "point": "...",
    "seg1": "...",
    "seg2": "...",
    "tol": "..."
  }
}
```