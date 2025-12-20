# unpin_elements

- カテゴリ: Misc
- 目的: 複数要素の `Pinned` フラグを一括で解除します。

## 概要
このコマンドは JSON-RPC で Revit MCP アドインに送信されます。`elementIds` 配列で指定された要素を 1 つのトランザクション内で順に処理し、ピン留めされているものだけを解除します。

すでにピン留めされていない要素はそのまま残り、`changed` 件数には含まれません。

## 使い方
- メソッド: unpin_elements

### パラメータ
| 名前        | 型    | 必須 | 既定値 |
|-------------|-------|------|--------|
| elementIds  | int[] | はい | []     |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "unpin_elements",
  "params": {
    "elementIds": [39399, 77829]
  }
}
```

### 結果の例
```json
{
  "ok": true,
  "requested": 2,
  "processed": 2,
  "changed": 0,
  "failedIds": []
}
```

## 関連コマンド
- get_joined_elements
- unpin_element
