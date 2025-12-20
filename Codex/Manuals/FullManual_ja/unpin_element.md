# unpin_element

- カテゴリ: Misc
- 目的: 指定した 1 つの要素について、`Pinned` フラグを解除します。

## 概要
このコマンドは JSON-RPC で Revit MCP アドインに送信されます。`elementId` または `uniqueId` で要素を特定し、ピン留めされている場合は Revit のトランザクション内で `Pinned = false` に変更します。

すでにピン留めされていない要素に対しては何も変更せず、`changed: false` を返します。

## 使い方
- メソッド: unpin_element

### パラメータ
| 名前       | 型     | 必須               | 既定値 |
|------------|--------|--------------------|--------|
| elementId  | int    | いいえ / いずれか一方 | 0      |
| uniqueId   | string | いいえ / いずれか一方 |        |

`elementId` と `uniqueId` のどちらか一方は必ず指定してください。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "unpin_element",
  "params": {
    "elementId": 39399
  }
}
```

### 結果の例
```json
{
  "ok": true,
  "elementId": 39399,
  "uniqueId": "f7d54af9-82bd-43de-9210-0e8af02555e6-000099e7",
  "changed": false,
  "wasPinned": false
}
```

## 関連コマンド
- get_joined_elements
- unpin_elements
