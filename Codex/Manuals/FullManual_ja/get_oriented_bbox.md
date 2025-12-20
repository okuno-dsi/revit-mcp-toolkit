# get_oriented_bbox

- カテゴリ: GetOrientedBoundingBoxHandler.cs
- 目的: このコマンドは『get_oriented_bbox』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_oriented_bbox

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| detailLevel | string | いいえ/状況による | fine |
| elementId | unknown | いいえ/状況による |  |
| includeCorners | bool | いいえ/状況による | true |
| strategy | string | いいえ/状況による | auto |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_oriented_bbox",
  "params": {
    "detailLevel": "...",
    "elementId": "...",
    "includeCorners": false,
    "strategy": "..."
  }
}
```