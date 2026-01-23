# delete_detail_line

- カテゴリ: AnnotationOps
- 目的: 詳細線（Detail Line）を削除します（複数対応）。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- 正式名（canonical）: `view.delete_detail_line`
- 旧名（deprecated alias）: `delete_detail_line`

### パラメータ
以下の **どちらか片方** を指定してください。

| 名前 | 型 | 必須 | 備考 |
|---|---|---:|---|
| elementId | int | 必須（どちらか） | 1本だけ削除 |
| elementIds | int[] | 必須（どちらか） | 複数まとめて削除（推奨） |

### リクエスト例（複数削除）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view.delete_detail_line",
  "params": {
    "elementIds": [123, 456, 789]
  }
}
```

## 関連コマンド
- get_detail_lines_in_view
- create_detail_line
- create_detail_arc
- move_detail_line
- rotate_detail_line
- get_line_styles
- set_detail_line_style
